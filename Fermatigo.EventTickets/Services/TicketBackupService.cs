using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Fermatigo.EventTickets.Services;

public record BackupResult(bool Ok, bool Sent, string Message);

/// <summary>
/// Záloha vstupenek mimo e-shop: celý seznam vstupenek se pošle do Google Tabulky
/// přes webovou aplikaci Google Apps Script. Kdyby e-shop nešel, vstupenky jde
/// zkontrolovat a odbavit podle tabulky.
/// </summary>
public class TicketBackupService
{
    /// <summary>Jednou za den se záloha pošle i beze změny, aby v tabulce bylo vidět, že e-shop žije.</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromHours(24);

    /// <summary>
    /// Sloupce zálohy. Prvních 12 je pro lidi, zbytek (technické údaje) potřebuje obnova vstupenek
    /// z Google Tabulky. Skript i obnova hledají sloupce podle názvu, ne podle pořadí.
    /// </summary>
    public static readonly string[] Header =
    {
        "Akce", "Objednávka", "Č. vstupenky", "Jméno", "E-mail", "Vstupenka", "VIP", "Kód", "Stav", "Vytvořeno", "Odbaveno", "Odbavil",
        ColOrderId, ColOrderItemId, ColProductId, ColEventId, ColCreatedUtc, ColCheckedInUtc, ColCanceled
    };

    public const string ColOrderId = "ID objednávky";
    public const string ColOrderItemId = "ID položky";
    public const string ColProductId = "ID produktu";
    public const string ColEventId = "ID akce";
    public const string ColCreatedUtc = "Vytvořeno UTC";
    public const string ColCheckedInUtc = "Odbaveno UTC";
    public const string ColCanceled = "Zneplatněná";

    private readonly EventTicketService _tickets;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly ISettingService _settingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public TicketBackupService(
        EventTicketService tickets,
        IOrderService orderService,
        IProductService productService,
        ISettingService settingService,
        IHttpClientFactory httpClientFactory,
        ILogger logger)
    {
        _tickets = tickets;
        _orderService = orderService;
        _productService = productService;
        _settingService = settingService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static string NewSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public static bool IsValidUrl(string? url)
        => Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && uri.Host.Equals("script.google.com", StringComparison.OrdinalIgnoreCase)
           && uri.AbsolutePath.EndsWith("/exec", StringComparison.Ordinal);

    private static string Time(DateTime? utc)
        => utc.HasValue ? EventTicketService.ToPrague(utc.Value).ToString("d. M. yyyy H:mm:ss", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Řádky tabulky ve stejném tvaru jako export do Excelu, navíc produkt a čas vytvoření.</summary>
    public async Task<List<string[]>> BuildRowsAsync()
    {
        var rows = new List<string[]>();
        var productNames = new Dictionary<int, string>();
        var infos = await _tickets.GetEventInfosAsync();
        var all = await _tickets.GetAllAsync();
        // objednávky po dávkách, ne dotaz na každou zvlášť
        var orders = await _tickets.OrderInfosAsync(all.Select(t => t.OrderId));
        foreach (var t in all)
        {
            var (number, orderCanceled) = await _tickets.OrderInfoAsync(t.OrderId, orders);
            if (!productNames.TryGetValue(t.ProductId, out var product))
                productNames[t.ProductId] = product = (await _productService.GetProductByIdAsync(t.ProductId))?.Name ?? string.Empty;
            var state = EventTicketService.StateLabel(t, orderCanceled);
            rows.Add(new[]
            {
                EventTicketService.InfoFor(infos, t.EventId).Name,
                number,
                t.Number.ToString(CultureInfo.InvariantCulture),
                t.HolderName,
                t.HolderEmail,
                product,
                EventTicketService.InfoFor(infos, t.EventId).IsVip(t.ProductId) ? "ano" : "ne",
                EventTicketService.FormatCode(t.Code),
                state,
                Time(t.CreatedOnUtc),
                Time(t.CheckedInOnUtc),
                t.CheckedInBy,
                t.OrderId.ToString(CultureInfo.InvariantCulture),
                t.OrderItemId.ToString(CultureInfo.InvariantCulture),
                t.ProductId.ToString(CultureInfo.InvariantCulture),
                t.EventId.ToString(CultureInfo.InvariantCulture),
                t.CreatedOnUtc.ToString("o", CultureInfo.InvariantCulture),
                t.CheckedInOnUtc?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
                t.Canceled ? "ano" : "ne"
            });
        }
        return rows;
    }

    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// Pošle zálohu, pokud se od minulé něco změnilo (nebo uplynul den, nebo force),
    /// nejčastěji jednou za interval z nastavení (BackupIntervalMinutes).
    /// Chybu jen zapíše, e-shop kvůli záloze nikdy nespadne.
    /// </summary>
    public async Task<BackupResult> RunAsync(bool force)
    {
        var settings = await _tickets.GetSettingsAsync();
        if (!IsValidUrl(settings.GoogleSheetUrl) || string.IsNullOrEmpty(settings.GoogleSheetSecret))
            return new BackupResult(false, false, "Záloha do Google Tabulky není nastavená.");

        // Interval z nastavení pluginu: plánovaná úloha běží v tomto intervalu (sekundy úlohy), pojistka pro dobu
        // do restartu aplikace, kdy nopCommerce ještě spouští úlohu podle starých sekund.
        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.BackupIntervalMinutes));
        if (!force && interval > TimeSpan.FromMinutes(1)
            && DateTime.TryParse(settings.BackupLastAttemptUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastAttempt)
            && DateTime.UtcNow - lastAttempt < interval - TimeSpan.FromSeconds(30))
            return new BackupResult(true, false, "Další záloha až po uplynutí intervalu.");

        var rows = await BuildRowsAsync();
        if (rows.Count == 0)
            return new BackupResult(true, false, "Zatím nejsou žádné vstupenky, není co zálohovat.");

        var events = (await _tickets.GetEventsAsync())
            .Select(e => new { id = e.Id, name = e.Name, date = e.EventDate, place = e.EventPlace, note = e.EventNote, productIds = e.ProductIds })
            .ToList();
        var hash = Hash(JsonSerializer.Serialize(events) + "\n" + JsonSerializer.Serialize(rows));
        DateTime.TryParse(settings.BackupLastSuccessUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastUtc);
        // rezerva 5 minut: při intervalu jednou denně úloha běží zhruba po 24 h a denní záloha se nesmí posunout na další den
        var fresh = lastUtc != default && DateTime.UtcNow - lastUtc < Heartbeat - TimeSpan.FromMinutes(5);
        if (!force && hash == settings.BackupLastHash && fresh && string.IsNullOrEmpty(settings.BackupLastError))
            return new BackupResult(true, false, "Beze změny, záloha je aktuální.");

        var payload = JsonSerializer.Serialize(new
        {
            secret = settings.GoogleSheetSecret,
            events,
            generated = Time(DateTime.UtcNow),
            header = Header,
            rows
        });

        settings.BackupLastAttemptUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        string error;
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            // Apps Script odpovídá přesměrováním na googleusercontent.com, HttpClient ho sám následuje.
            using var response = await client.PostAsync(settings.GoogleSheetUrl.Trim(), content);
            var body = await response.Content.ReadAsStringAsync();
            error = ParseResponse(response.IsSuccessStatusCode, (int)response.StatusCode, body);
        }
        catch (Exception ex)
        {
            error = "Google Tabulka neodpověděla: " + ex.Message;
        }

        if (error.Length == 0)
        {
            settings.BackupLastHash = hash;
            settings.BackupLastSuccessUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            settings.BackupLastError = string.Empty;
            await _settingService.SaveSettingAsync(settings);
            return new BackupResult(true, true, $"Záloha odeslaná, vstupenek: {rows.Count}.");
        }

        // Stejnou chybu do protokolu nepsat každou minutu znovu.
        if (settings.BackupLastError != error)
            await _logger.WarningAsync("Fermatigo vstupenky, záloha do Google Tabulky: " + error);
        settings.BackupLastError = error;
        await _settingService.SaveSettingAsync(settings);
        return new BackupResult(false, false, error);
    }

    private static string ParseResponse(bool success, int status, string body)
    {
        if (!success)
            return $"Google Tabulka vrátila chybu HTTP {status}.";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
                return string.Empty;
            var message = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            return message == "secret"
                ? "Tajný klíč ve skriptu nesedí. Vložte do Apps Script znovu skript z této stránky a nasaďte novou verzi."
                : "Skript v Google Tabulce hlásí chybu: " + (message ?? "neznámá");
        }
        catch (JsonException)
        {
            // Místo JSON přišla HTML stránka: typicky přihlášení Google, protože webová aplikace není přístupná všem.
            return "Google místo zálohy vrátil webovou stránku. Zkontrolujte, že webová aplikace je nasazená s přístupem „Kdokoli“ a adresa končí na /exec.";
        }
    }

    #region Obnova z Google Tabulky

    /// <summary>
    /// Načte archiv vstupenek a akcí z Google Tabulky (list „Archiv vstupenek“ a „Akce“).
    /// Archiv obsahuje každou vstupenku, která kdy v e-shopu byla, i když z e-shopu zmizela.
    /// </summary>
    public async Task<(ArchiveData? Data, string Error)> ReadArchiveAsync()
    {
        var settings = await _tickets.GetSettingsAsync();
        if (!IsValidUrl(settings.GoogleSheetUrl) || string.IsNullOrEmpty(settings.GoogleSheetSecret))
            return (null, "Záloha do Google Tabulky není nastavená. Vyplňte adresu skriptu a tajný klíč.");

        string body;
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(90);
            using var content = new StringContent(JsonSerializer.Serialize(new { secret = settings.GoogleSheetSecret, action = "read" }),
                Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(settings.GoogleSheetUrl.Trim(), content);
            body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return (null, $"Google Tabulka vrátila chybu HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return (null, "Google Tabulka neodpověděla: " + ex.Message);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!(root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True))
            {
                var message = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                return (null, message == "secret"
                    ? "Tajný klíč nesedí se skriptem v Google Tabulce. Opište klíč ze skriptu (řádek const SECRET) do pole Tajný klíč."
                    // Starý skript (do 1.7) zná jen zápis, požadavek na čtení skončí chybou TypeError.
                    : message == "action" || (message ?? string.Empty).Contains("TypeError", StringComparison.Ordinal)
                        ? "Skript v Google Tabulce je starší a obnovu neumí. Zkopírujte skript z této stránky (Jak zálohu nastavit → Kopírovat skript), vložte ho do Apps Script místo starého, uložte a nasaďte novou verzi: Nasadit → Spravovat nasazení → ikona tužky → Verze: Nová verze → Nasadit. Adresa zůstane stejná."
                        : "Skript v Google Tabulce hlásí chybu: " + (message ?? "neznámá"));
            }

            var data = new ArchiveData();
            var header = root.GetProperty("header").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList();
            int Col(string name) => header.IndexOf(name);
            string Cell(JsonElement row, int i) => i >= 0 && i < row.GetArrayLength() ? row[i].ToString() : string.Empty;
            int Int(JsonElement row, int i) => int.TryParse(Cell(row, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
            DateTime? Utc(JsonElement row, int i) => DateTime.TryParse(Cell(row, i), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var v) ? v : null;

            int cCode = Col("Kód"), cEvent = Col("Akce"), cOrder = Col("Objednávka"), cNumber = Col("Č. vstupenky"),
                cName = Col("Jméno"), cEmail = Col("E-mail"), cState = Col("Stav"), cBy = Col("Odbavil"),
                cOrderId = Col(ColOrderId), cItem = Col(ColOrderItemId), cProduct = Col(ColProductId), cEventId = Col(ColEventId),
                cCreated = Col(ColCreatedUtc), cChecked = Col(ColCheckedInUtc), cCanceled = Col(ColCanceled),
                cLast = Col("Naposledy v e-shopu");
            if (cCode < 0 || cOrderId < 0)
                return (null, "V Google Tabulce chybí technické sloupce pro obnovu. Záloha z verze starší než 1.8.0 obnovu neumožňuje, nechte proběhnout novou zálohu.");

            foreach (var row in root.GetProperty("rows").EnumerateArray())
            {
                var code = EventTicketService.NormalizeCode(Cell(row, cCode));
                if (code.Length == 0)
                    continue;
                data.Tickets.Add(new ArchivedTicket
                {
                    Code = code,
                    EventName = Cell(row, cEvent),
                    OrderNumber = Cell(row, cOrder),
                    Number = Int(row, cNumber),
                    HolderName = Cell(row, cName),
                    HolderEmail = Cell(row, cEmail),
                    State = Cell(row, cState),
                    CheckedInBy = Cell(row, cBy),
                    OrderId = Int(row, cOrderId),
                    OrderItemId = Int(row, cItem),
                    ProductId = Int(row, cProduct),
                    EventId = Int(row, cEventId),
                    CreatedOnUtc = Utc(row, cCreated) ?? DateTime.UtcNow,
                    CheckedInOnUtc = Utc(row, cChecked),
                    Canceled = Cell(row, cCanceled) == "ano",
                    LastSeen = Cell(row, cLast)
                });
            }

            if (root.TryGetProperty("events", out var evs) && evs.ValueKind == JsonValueKind.Array)
                foreach (var ev in evs.EnumerateArray())
                    data.Events.Add(new ArchivedEvent
                    {
                        Id = ev.TryGetProperty("id", out var id) && int.TryParse(id.ToString(), out var i) ? i : 0,
                        Name = ev.TryGetProperty("name", out var n) ? n.ToString() : string.Empty,
                        Date = ev.TryGetProperty("date", out var d) ? d.ToString() : string.Empty,
                        Place = ev.TryGetProperty("place", out var pl) ? pl.ToString() : string.Empty,
                        Note = ev.TryGetProperty("note", out var no) ? no.ToString() : string.Empty,
                        ProductIds = ev.TryGetProperty("productIds", out var pr) ? pr.ToString() : string.Empty
                    });
            return (data, string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, "Google místo dat vrátil něco jiného. Zkontrolujte, že webová aplikace je nasazená s přístupem „Kdokoli“ a že je v Apps Script aktuální skript z této stránky.");
        }
    }

    #endregion

    /// <summary>Skript pro Google Apps Script s tajným klíčem tohoto e-shopu.</summary>
    public static string AppsScript(string secret) => AppsScriptTemplate.Replace("__SECRET__", secret);

    private const string AppsScriptTemplate = """
// Záloha vstupenek z e-shopu (plugin Fermatigo vstupenky s QR kódem, autor Tom Tomčo).
// List „Vstupenky“: aktuální stav vstupenek v e-shopu, přepíše se při každé záloze.
// List „Archiv vstupenek“: každá vstupenka, která kdy v e-shopu byla. Nic se z něj nemaže,
//   jen se aktualizuje, a vstupenky, které z e-shopu zmizely, mají ve sloupci „V e-shopu“ hodnotu „ne“.
//   Z tohoto listu umí plugin vstupenky obnovit (Konfigurace pluginu, Obnovit z Google Tabulky).
// List „Akce“: údaje akcí pro obnovu. List „Info“: čas poslední zálohy a přehled.
const SECRET = '__SECRET__';
const ARCHIVE = 'Archiv vstupenek';

function doPost(e) {
  const lock = LockService.getScriptLock();
  lock.waitLock(30000);
  try {
    const data = JSON.parse(e.postData.contents);
    if (data.secret !== SECRET) return reply({ ok: false, error: 'secret' });
    const ss = SpreadsheetApp.getActiveSpreadsheet();
    if (data.action === 'read') return reply(readArchive(ss));
    if (data.action) return reply({ ok: false, error: 'action' });

    writeTable(ss, 'Vstupenky', data.header, data.rows, 0);
    const missing = upsertArchive(ss, data.header, data.rows, data.generated);
    upsertEvents(ss, data.events || []);

    const iEvent = data.header.indexOf('Akce'), iState = data.header.indexOf('Stav');
    const lines = [
      ['Poslední záloha', data.generated, '', ''],
      ['Vstupenek v e-shopu', String(data.rows.length), '', ''],
      ['Odbaveno', String(data.rows.filter(r => r[iState] === 'odbavená').length), '', ''],
      ['V archivu, ale chybí v e-shopu', String(missing), '', ''],
      ['', '', '', ''],
      ['Akce', 'Datum a místo', 'Vstupenek', 'Odbaveno']
    ];
    (data.events || []).forEach(ev => {
      const mine = data.rows.filter(r => r[iEvent] === ev.name);
      lines.push([ev.name, [ev.date, ev.place].filter(String).join(', '), String(mine.length),
        String(mine.filter(r => r[iState] === 'odbavená').length)]);
    });
    writeTable(ss, 'Info', null, lines, 99);
    return reply({ ok: true, count: data.rows.length, missing: missing });
  } catch (err) {
    return reply({ ok: false, error: String(err) });
  } finally {
    lock.releaseLock();
  }
}

function writeTable(ss, name, header, rows, position) {
  const sheet = ss.getSheetByName(name) || ss.insertSheet(name, Math.min(position, ss.getSheets().length));
  sheet.clearContents();
  const table = header ? [header].concat(rows) : rows;
  if (!table.length) return sheet;
  sheet.getRange(1, 1, table.length, table[0].length).setNumberFormat('@').setValues(table);
  if (header) {
    sheet.getRange(1, 1, 1, header.length).setFontWeight('bold');
    sheet.setFrozenRows(1);
  }
  return sheet;
}

// Archiv: řádky se podle kódu aktualizují nebo přidají, nikdy se nemažou.
function upsertArchive(ss, header, rows, generated) {
  const head = header.concat(['Naposledy v e-shopu', 'V e-shopu']);
  const iCode = header.indexOf('Kód');
  const sheet = ss.getSheetByName(ARCHIVE) || ss.insertSheet(ARCHIVE, 1);
  const old = sheet.getLastRow() > 1 ? sheet.getRange(1, 1, sheet.getLastRow(), sheet.getLastColumn()).getValues() : [];
  const oldHead = old.length ? old[0].map(String) : head;
  const byCode = {};
  const out = [];
  old.slice(1).forEach(r => {
    // starší archiv s jiným pořadím sloupců převede podle názvů
    const row = head.map(h => { const i = oldHead.indexOf(h); return i >= 0 ? String(r[i]) : ''; });
    if (!row[iCode]) return;
    row[head.length - 1] = 'ne';
    byCode[row[iCode]] = out.length;
    out.push(row);
  });
  rows.forEach(r => {
    const row = r.map(String).concat([generated, 'ano']);
    if (row[iCode] in byCode) out[byCode[row[iCode]]] = row;
    else { byCode[row[iCode]] = out.length; out.push(row); }
  });
  writeTable(ss, ARCHIVE, head, out, 1);
  return out.filter(r => r[head.length - 1] === 'ne').length;
}

function upsertEvents(ss, events) {
  const head = ['ID akce', 'Název', 'Datum', 'Místo', 'Poznámka', 'ID produktů'];
  const sheet = ss.getSheetByName('Akce') || ss.insertSheet('Akce');
  const old = sheet.getLastRow() > 1 ? sheet.getRange(2, 1, sheet.getLastRow() - 1, head.length).getValues() : [];
  const byId = {};
  const out = old.map(r => r.map(String)).filter(r => r[0]);
  out.forEach((r, i) => byId[r[0]] = i);
  events.forEach(ev => {
    const row = [String(ev.id), ev.name || '', ev.date || '', ev.place || '', ev.note || '', ev.productIds || ''];
    if (row[0] in byId) out[byId[row[0]]] = row; else { byId[row[0]] = out.length; out.push(row); }
  });
  writeTable(ss, 'Akce', head, out, 2);
}

function readArchive(ss) {
  const sheet = ss.getSheetByName(ARCHIVE);
  if (!sheet || sheet.getLastRow() < 1) return { ok: true, header: [], rows: [], events: [] };
  const all = sheet.getRange(1, 1, sheet.getLastRow(), sheet.getLastColumn()).getValues().map(r => r.map(String));
  const ev = ss.getSheetByName('Akce');
  const events = ev && ev.getLastRow() > 1
    ? ev.getRange(2, 1, ev.getLastRow() - 1, 6).getValues().map(r => ({
        id: String(r[0]), name: String(r[1]), date: String(r[2]), place: String(r[3]), note: String(r[4]), productIds: String(r[5]) }))
    : [];
  return { ok: true, header: all[0], rows: all.slice(1), events: events };
}

function doGet() {
  return reply({ ok: true, info: 'Záloha vstupenek běží. Data se posílají metodou POST.' });
}

function reply(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}
""";
}

public class ArchiveData
{
    public List<ArchivedTicket> Tickets { get; } = new();
    public List<ArchivedEvent> Events { get; } = new();
}

public class ArchivedTicket
{
    public string Code { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public int Number { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string HolderEmail { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string CheckedInBy { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }
    public int EventId { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? CheckedInOnUtc { get; set; }
    public bool Canceled { get; set; }
    public string LastSeen { get; set; } = string.Empty;
}

public class ArchivedEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Place { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string ProductIds { get; set; } = string.Empty;
}
