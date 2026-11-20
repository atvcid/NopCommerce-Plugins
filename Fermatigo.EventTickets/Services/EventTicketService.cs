using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fermatigo.EventTickets.Domain;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Stores;
using QRCoder;

namespace Fermatigo.EventTickets.Services;

public enum CheckInStatus
{
    CheckedIn,
    AlreadyUsed,
    Canceled,
    NotFound
}

public record TicketStats(int Total, int CheckedIn, int Canceled);

/// <summary>Údaje akce pro e-mail, PDF a stránky. Bez akce (neznámá nebo smazaná) obecná „Vstupenka“.</summary>
public record EventInfo(int Id, string Name, string Date, string Place, string Note)
{
    public string When => string.Join(", ", new[] { Date, Place }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>Produkty VIP vstupenek akce.</summary>
    public IReadOnlyList<int> VipProductIds { get; init; } = Array.Empty<int>();

    public bool IsVip(int productId) => VipProductIds.Contains(productId);

    public static EventInfo From(FmEvent? e) => e == null
        ? new EventInfo(0, "Vstupenka", string.Empty, string.Empty, string.Empty)
        : new EventInfo(e.Id, string.IsNullOrWhiteSpace(e.Name) ? "Vstupenka" : e.Name.Trim(),
            e.EventDate?.Trim() ?? string.Empty, e.EventPlace?.Trim() ?? string.Empty, e.EventNote?.Trim() ?? string.Empty)
        {
            VipProductIds = EventTicketService.ParseProductIds(e.VipProductIds)
        };
}

/// <summary>Vytváření, odbavování a rozesílání vstupenek.</summary>
public class EventTicketService
{
    private readonly IRepository<FmEventTicket> _tickets;
    private readonly IRepository<FmEvent> _events;
    private readonly IRepository<Download> _downloads;
    private readonly IRepository<QueuedEmail> _queuedEmails;
    private readonly IRepository<Product> _products;
    private readonly IOrderService _orderService;
    private readonly IAddressService _addressService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly ISettingService _settingService;
    private readonly IStoreService _storeService;
    private readonly IQueuedEmailService _queuedEmailService;
    private readonly IEmailAccountService _emailAccountService;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly IDownloadService _downloadService;
    private readonly INopFileProvider _fileProvider;
    private readonly ILogger _logger;

    public EventTicketService(
        IRepository<FmEventTicket> tickets,
        IRepository<FmEvent> events,
        IRepository<Download> downloads,
        IRepository<QueuedEmail> queuedEmails,
        IRepository<Product> products,
        IOrderService orderService,
        IAddressService addressService,
        ICustomerService customerService,
        IProductService productService,
        ISettingService settingService,
        IStoreService storeService,
        IQueuedEmailService queuedEmailService,
        IEmailAccountService emailAccountService,
        EmailAccountSettings emailAccountSettings,
        IDownloadService downloadService,
        INopFileProvider fileProvider,
        ILogger logger)
    {
        _tickets = tickets;
        _events = events;
        _downloads = downloads;
        _queuedEmails = queuedEmails;
        _products = products;
        _orderService = orderService;
        _addressService = addressService;
        _customerService = customerService;
        _productService = productService;
        _settingService = settingService;
        _storeService = storeService;
        _queuedEmailService = queuedEmailService;
        _emailAccountService = emailAccountService;
        _emailAccountSettings = emailAccountSettings;
        _downloadService = downloadService;
        _fileProvider = fileProvider;
        _logger = logger;
    }

    #region Pomocné

    /// <summary>Vytváření vstupenek jen jedno po druhém: souběžná platba a ruční doplnění by jinak mohly vstupenky zdvojit.</summary>
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    /// <summary>E-mail s PDF a úklid starých PDF příloh se nesmí potkat (příloha vzniká těsně před e-mailem).</summary>
    private static readonly SemaphoreSlim EmailLock = new(1, 1);

    private static readonly TimeZoneInfo? Prague = FindPrague();

    private static TimeZoneInfo? FindPrague()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"); }
        catch { return null; }
    }

    /// <summary>Text do jednoho řádku a na nejvýš max znaků (sloupce v databázi, předmět e-mailu).</summary>
    public static string OneLine(string? value, int max)
    {
        var s = string.Join(" ", (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>
    /// Čísla objednávek a storno pro víc objednávek najednou (jeden dotaz na 500 objednávek místo dotazu na každou).
    /// Výsledek slouží jako mezipaměť pro <see cref="OrderInfoAsync"/>.
    /// </summary>
    public async Task<Dictionary<int, (string, bool)>> OrderInfosAsync(IEnumerable<int> orderIds)
    {
        var cache = new Dictionary<int, (string, bool)>();
        foreach (var chunk in orderIds.Where(id => id > 0).Distinct().Chunk(500))
            foreach (var order in await _orderService.GetOrdersByIdsAsync(chunk))
                cache[order.Id] = (order.CustomOrderNumber ?? order.Id.ToString(), order.OrderStatus == OrderStatus.Cancelled);
        return cache;
    }

    public static IReadOnlyList<int> ParseProductIds(string? value)
        => (value ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    public static string NewCode()
    {
        var alphabet = EventTicketsDefaults.CodeAlphabet;
        var sb = new StringBuilder(EventTicketsDefaults.CodeLength);
        for (var i = 0; i < EventTicketsDefaults.CodeLength; i++)
            sb.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        return sb.ToString();
    }

    /// <summary>Stav vstupenky slovem. Stornovaná objednávka má přednost: „storno“.</summary>
    public static string StateLabel(FmEventTicket t, bool orderCanceled)
        => orderCanceled ? "storno" : t.Canceled ? "neplatná" : t.CheckedInOnUtc.HasValue ? "odbavená" : "platná";

    /// <summary>Číslo objednávky a jestli je stornovaná, s mezipamětí pro výpis seznamů.</summary>
    public async Task<(string Number, bool Canceled)> OrderInfoAsync(int orderId, IDictionary<int, (string, bool)> cache)
    {
        if (cache.TryGetValue(orderId, out var hit))
            return hit;
        var order = await _orderService.GetOrderByIdAsync(orderId);
        var info = (order?.CustomOrderNumber ?? orderId.ToString(), order?.OrderStatus == OrderStatus.Cancelled);
        cache[orderId] = info;
        return info;
    }

    /// <summary>Kód pro čtení člověkem: ABCDE FGHJK MNPQR STUVW.</summary>
    public static string FormatCode(string code)
        => string.Join(" ", Enumerable.Range(0, (code.Length + 4) / 5)
            .Select(i => code.Substring(i * 5, Math.Min(5, code.Length - i * 5))));

    public static string NormalizeCode(string? code)
        => new string((code ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public Task<EventTicketsSettings> GetSettingsAsync() => _settingService.LoadSettingAsync<EventTicketsSettings>();

    public async Task<string> GetStoreUrlAsync(int storeId)
    {
        var store = await _storeService.GetStoreByIdAsync(storeId)
                    ?? (await _storeService.GetAllStoresAsync()).FirstOrDefault();
        var url = store?.Url ?? "/";
        return url.EndsWith('/') ? url : url + "/";
    }

    public static string TicketUrl(string storeUrl, string code) => storeUrl + "vstupenka/" + code;
    public static string OrderTicketsUrl(string storeUrl, Guid orderGuid) => storeUrl + "vstupenky/" + orderGuid.ToString("D");

    public static byte[] QrPng(string content, int pixelsPerModule = 12)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    /// <summary>Čas pro zobrazení v českém čase (server běží v UTC).</summary>
    public static DateTime ToPrague(DateTime utc)
    {
        return Prague == null
            ? utc.ToLocalTime()
            : TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Prague);
    }

    #endregion

    #region Akce

    public async Task<IList<FmEvent>> GetEventsAsync()
        => await _events.Table.OrderBy(e => e.Id).ToListAsync();

    public async Task<FmEvent?> GetEventByIdAsync(int id)
        => id <= 0 ? null : await _events.Table.FirstOrDefaultAsync(e => e.Id == id);

    public async Task InsertEventAsync(FmEvent e) => await _events.InsertAsync(e, false);
    public async Task UpdateEventAsync(FmEvent e) => await _events.UpdateAsync(e, false);
    public async Task DeleteEventAsync(FmEvent e) => await _events.DeleteAsync(e, false);

    /// <summary>
    /// Nevratně smaže vstupenky a do poznámek jejich objednávek zapíše, které a kdo je smazal.
    /// QR kódy smazaných vstupenek u vstupu ukážou „Neznámá vstupenka“.
    /// </summary>
    public async Task<int> DeleteTicketsAsync(IList<FmEventTicket> tickets, string who, string reason)
    {
        foreach (var t in tickets)
            await _tickets.DeleteAsync(t, false);
        foreach (var group in tickets.GroupBy(t => t.OrderId))
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = group.Key,
                Note = $"Smazáno vstupenek: {group.Count()} ({string.Join(", ", group.OrderBy(t => t.Number).Select(t => $"č. {t.Number}, kód {FormatCode(t.Code)}"))}). Smazal: {who}. {reason}",
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });
        return tickets.Count;
    }

    /// <summary>Kódy všech vstupenek v e-shopu (pro porovnání s archivem v Google Tabulce).</summary>
    public async Task<HashSet<string>> GetAllCodesAsync()
        => (await _tickets.Table.Select(t => t.Code).ToListAsync()).ToHashSet();

    /// <summary>
    /// Kódy vstupenek, které někdo ručně smazal v administraci (podle poznámek objednávky
    /// „Smazáno vstupenek: … kód XXXXX …“). Při obnově z Google Tabulky se nabízí nezaškrtnuté.
    /// </summary>
    public async Task<HashSet<string>> GetManuallyDeletedCodesAsync(IEnumerable<int> orderIds)
    {
        var codes = new HashSet<string>();
        foreach (var orderId in orderIds.Distinct())
            foreach (var note in await _orderService.GetOrderNotesByOrderIdAsync(orderId))
            {
                if (!note.Note.StartsWith("Smazáno vstupenek:", StringComparison.Ordinal))
                    continue;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(note.Note, @"kód ([A-Z0-9 ]+?)\)|kód ([A-Z0-9 ]+?),"))
                    codes.Add(NormalizeCode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
            }
        return codes;
    }

    /// <summary>
    /// Obnoví vstupenky z archivu v Google Tabulce s původními kódy (QR kódy dál platí), stavem odbavení
    /// a zneplatnění. Existující vstupenky nikdy nepřepíše. Akci dohledá podle názvu, případně ji založí
    /// z archivu. Vrací počet obnovených.
    /// </summary>
    public async Task<int> RestoreFromArchiveAsync(ArchiveData archive, ICollection<string> codes, string who)
    {
        var existing = await GetAllCodesAsync();
        var events = await GetEventsAsync();
        var eventMap = new Dictionary<int, int>();
        var restored = new List<FmEventTicket>();

        async Task<int> EventIdForAsync(ArchivedTicket a)
        {
            if (eventMap.TryGetValue(a.EventId, out var mapped))
                return mapped;
            var source = archive.Events.FirstOrDefault(e => e.Id == a.EventId);
            var name = (source?.Name ?? a.EventName).Trim();
            var match = events.FirstOrDefault(e => e.Id == a.EventId && string.Equals(e.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase))
                        ?? events.FirstOrDefault(e => string.Equals(e.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase));
            if (match == null && name.Length > 0 && name != "Vstupenka")
            {
                // Akce v e-shopu chybí: založí se z archivu, jen s produkty, které nepatří jiné akci.
                var used = events.SelectMany(e => ParseProductIds(e.ProductIds)).ToHashSet();
                match = new FmEvent
                {
                    Name = OneLine(name, 400),
                    EventDate = OneLine(source?.Date, 400),
                    EventPlace = OneLine(source?.Place, 400),
                    EventNote = source?.Note ?? string.Empty,
                    ProductIds = string.Join(",", ParseProductIds(source?.ProductIds).Where(id => !used.Contains(id))),
                    CreatedOnUtc = DateTime.UtcNow
                };
                await InsertEventAsync(match);
                events.Add(match);
            }
            eventMap[a.EventId] = match?.Id ?? 0;
            return eventMap[a.EventId];
        }

        // Tabulka je mimo e-shop a může ji někdo upravit: bere se jen kód ve tvaru vstupenky, texty se zkrátí na sloupce.
        foreach (var a in archive.Tickets.Where(t => codes.Contains(t.Code) && !existing.Contains(t.Code)
                     && (t.Code.Length == EventTicketsDefaults.CodeLength || t.Code.Length == EventTicketsDefaults.LegacyCodeLength)))
        {
            var ticket = new FmEventTicket
            {
                Code = a.Code,
                OrderId = Math.Max(0, a.OrderId),
                OrderItemId = Math.Max(0, a.OrderItemId),
                ProductId = Math.Max(0, a.ProductId),
                EventId = await EventIdForAsync(a),
                Number = Math.Max(0, a.Number),
                HolderName = OneLine(a.HolderName, 400),
                HolderEmail = OneLine(a.HolderEmail, 400),
                CreatedOnUtc = a.CreatedOnUtc,
                CheckedInOnUtc = a.CheckedInOnUtc,
                CheckedInBy = a.CheckedInOnUtc.HasValue ? OneLine(a.CheckedInBy, 400) : string.Empty,
                Canceled = a.Canceled
            };
            await _tickets.InsertAsync(ticket, false);
            existing.Add(a.Code);
            restored.Add(ticket);
        }

        foreach (var group in restored.GroupBy(t => t.OrderId))
            if (await _orderService.GetOrderByIdAsync(group.Key) != null)
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = group.Key,
                    Note = $"Obnoveno vstupenek z Google Tabulky: {group.Count()} ({string.Join(", ", group.OrderBy(t => t.Number).Select(t => $"č. {t.Number}, kód {FormatCode(t.Code)}"))}). Obnovil: {who}.",
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
        return restored.Count;
    }

    public async Task<IList<FmEventTicket>> GetByEventAsync(int eventId)
        => await _tickets.Table.Where(t => t.EventId == eventId).ToListAsync();

    /// <summary>Akce, ke které produkt patří (jeden produkt = jedna akce).</summary>
    public static FmEvent? EventForProduct(IEnumerable<FmEvent> events, int productId)
        => events.FirstOrDefault(e => ParseProductIds(e.ProductIds).Contains(productId));

    /// <summary>Údaje akcí podle ID pro rychlé vykreslení seznamů.</summary>
    public async Task<Dictionary<int, EventInfo>> GetEventInfosAsync()
        => (await GetEventsAsync()).ToDictionary(e => e.Id, EventInfo.From);

    public static EventInfo InfoFor(IDictionary<int, EventInfo> infos, int eventId)
        => infos.TryGetValue(eventId, out var info) ? info : EventInfo.From(null);

    /// <summary>
    /// Převod z verze s jednou akcí: když ještě žádná akce není a v nastavení je vyplněná,
    /// založí z ní první akci. Vstupenkám bez akce doplní akci podle produktu.
    /// Volání je bezpečné opakovat.
    /// </summary>
    public async Task EnsureEventsMigratedAsync()
    {
        // Stačí jednou za běh aplikace, ne při každém otevření administrace.
        if (_eventsMigrated)
            return;
        await EnsureEventsMigratedCoreAsync();
        _eventsMigrated = true;
    }

    private static volatile bool _eventsMigrated;

    private async Task EnsureEventsMigratedCoreAsync()
    {
        var events = await GetEventsAsync();
        if (events.Count == 0)
        {
            var settings = await GetSettingsAsync();
            if (!string.IsNullOrWhiteSpace(settings.EventName) || ParseProductIds(settings.ProductIds).Count > 0)
            {
                var first = new FmEvent
                {
                    Name = settings.EventName?.Trim() ?? string.Empty,
                    EventDate = settings.EventDate?.Trim() ?? string.Empty,
                    EventPlace = settings.EventPlace?.Trim() ?? string.Empty,
                    EventNote = settings.EventNote?.Trim() ?? string.Empty,
                    ProductIds = string.Join(",", ParseProductIds(settings.ProductIds)),
                    CreatedOnUtc = DateTime.UtcNow
                };
                await InsertEventAsync(first);
                events.Add(first);
            }
        }

        if (events.Count == 0)
            return;
        var orphans = await _tickets.Table.Where(t => t.EventId == 0).ToListAsync();
        foreach (var t in orphans)
        {
            t.EventId = (EventForProduct(events, t.ProductId) ?? events[0]).Id;
            await _tickets.UpdateAsync(t, false);
        }
    }

    #endregion

    #region Čtení

    public async Task<FmEventTicket?> GetByCodeAsync(string? code)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length != EventTicketsDefaults.CodeLength && normalized.Length != EventTicketsDefaults.LegacyCodeLength)
            return null;
        return await _tickets.Table.FirstOrDefaultAsync(t => t.Code == normalized);
    }

    /// <summary>Nový kód, který ještě žádná vstupenka nemá (krátký kód se může náhodou zopakovat).</summary>
    private async Task<string> NewUniqueCodeAsync(ISet<string> reserved)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var code = NewCode();
            if (!reserved.Contains(code) && !await _tickets.Table.AnyAsync(t => t.Code == code))
            {
                reserved.Add(code);
                return code;
            }
        }
        throw new InvalidOperationException("Nepodařilo se vytvořit jedinečný kód vstupenky.");
    }

    public async Task<FmEventTicket?> GetByIdAsync(int id)
        => await _tickets.Table.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IList<FmEventTicket>> GetByOrderAsync(int orderId)
        => await _tickets.Table.Where(t => t.OrderId == orderId).OrderBy(t => t.Number).ToListAsync();

    public async Task<(IList<FmEventTicket> Rows, int Total)> SearchAsync(string? query, int eventId, int page, int pageSize)
    {
        var q = _tickets.Table;
        if (eventId > 0)
            q = q.Where(t => t.EventId == eventId);
        var text = (query ?? string.Empty).Trim();
        if (text.Length > 0)
        {
            var code = NormalizeCode(text);
            var lower = text.ToLower();
            var orderIds = new List<int>();
            var order = await _orderService.GetOrderByCustomOrderNumberAsync(text);
            if (order != null)
                orderIds.Add(order.Id);
            if (int.TryParse(text, out var number) && number > 0)
                orderIds.Add(number);
            q = q.Where(t => t.HolderName.ToLower().Contains(lower)
                             || t.HolderEmail.ToLower().Contains(lower)
                             || (code.Length >= 4 && t.Code.Contains(code))
                             || orderIds.Contains(t.OrderId));
        }

        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(t => t.OrderId).ThenBy(t => t.Number)
            .Skip(Math.Max(0, page) * pageSize).Take(pageSize).ToListAsync();
        return (rows, total);
    }

    public async Task<IList<FmEventTicket>> GetAllAsync()
        => await _tickets.Table.OrderBy(t => t.OrderId).ThenBy(t => t.Number).ToListAsync();

    public async Task<TicketStats> GetStatsAsync(int eventId = 0)
    {
        var q = eventId > 0 ? _tickets.Table.Where(t => t.EventId == eventId) : _tickets.Table;
        var total = await q.CountAsync(t => !t.Canceled);
        var checkedIn = await q.CountAsync(t => !t.Canceled && t.CheckedInOnUtc != null);
        var canceled = await q.CountAsync(t => t.Canceled);
        return new TicketStats(total, checkedIn, canceled);
    }

    public async Task<int> CountTicketsAsync(int eventId)
        => await _tickets.Table.CountAsync(t => t.EventId == eventId);

    #endregion

    #region Vytvoření a stav

    /// <summary>
    /// Doplní vstupenky k zaplacené objednávce: jeden kus vstupenkového produktu = jedna vstupenka.
    /// Volání je bezpečné opakovat, už vytvořené vstupenky se nezdvojí. Vrací nově vytvořené.
    /// </summary>
    public async Task<IList<FmEventTicket>> CreateTicketsAsync(Order order)
    {
        await CreateLock.WaitAsync();
        try
        {
            return await CreateTicketsCoreAsync(order);
        }
        finally
        {
            CreateLock.Release();
        }
    }

    private async Task<IList<FmEventTicket>> CreateTicketsCoreAsync(Order order)
    {
        var created = new List<FmEventTicket>();
        var events = await GetEventsAsync();
        var productIds = events.SelectMany(e => ParseProductIds(e.ProductIds)).Distinct().ToList();
        if (productIds.Count == 0 || order.Deleted)
            return created;

        var items = (await _orderService.GetOrderItemsAsync(order.Id))
            .Where(i => productIds.Contains(i.ProductId))
            .OrderBy(i => i.Id)
            .ToList();
        if (items.Count == 0)
            return created;

        var existing = await GetByOrderAsync(order.Id);
        var (name, email) = await GetHolderAsync(order);
        var number = existing.Count == 0 ? 0 : existing.Max(t => t.Number);
        var reserved = new HashSet<string>();

        foreach (var item in items)
        {
            var missing = item.Quantity - existing.Count(t => t.OrderItemId == item.Id);
            for (var i = 0; i < missing; i++)
            {
                var ticket = new FmEventTicket
                {
                    Code = await NewUniqueCodeAsync(reserved),
                    OrderId = order.Id,
                    OrderItemId = item.Id,
                    ProductId = item.ProductId,
                    EventId = EventForProduct(events, item.ProductId)?.Id ?? 0,
                    Number = ++number,
                    HolderName = name,
                    HolderEmail = email,
                    CreatedOnUtc = DateTime.UtcNow,
                    CheckedInBy = string.Empty
                };
                await _tickets.InsertAsync(ticket, false);
                created.Add(ticket);
            }
        }

        if (created.Count > 0)
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = $"Vytvořeno vstupenek: {created.Count} (plugin Fermatigo vstupenky).",
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

        return created;
    }

    private async Task<(string Name, string Email)> GetHolderAsync(Order order)
    {
        var address = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
        var name = $"{address?.FirstName} {address?.LastName}".Trim();
        if (name.Length == 0 && customer != null)
            name = ((await _customerService.GetCustomerFullNameAsync(customer)) ?? string.Empty).Trim();
        var email = address?.Email;
        if (string.IsNullOrWhiteSpace(email))
            email = customer?.Email ?? string.Empty;
        return (name, email.Trim());
    }

    /// <summary>Odbavení: povede se jen jednou, i když stejný kód načtou dva lidé současně.</summary>
    public async Task<(CheckInStatus Status, FmEventTicket? Ticket)> CheckInAsync(string? code, string checkedInBy)
    {
        var ticket = await GetByCodeAsync(code);
        if (ticket == null)
            return (CheckInStatus.NotFound, null);
        if (ticket.Canceled)
            return (CheckInStatus.Canceled, ticket);

        var now = DateTime.UtcNow;
        var by = checkedInBy.Length > 400 ? checkedInBy[..400] : checkedInBy;
        var id = ticket.Id;
        // Podmíněný UPDATE v databázi: projde jen u dosud neodbavené vstupenky.
        var pending = _tickets.Table.Where(t => t.Id == id && t.CheckedInOnUtc == null && !t.Canceled);
        var updated = await LinqToDB.LinqExtensions.UpdateAsync(
            LinqToDB.LinqExtensions.Set(LinqToDB.LinqExtensions.Set(pending, t => t.CheckedInOnUtc, (DateTime?)now), t => t.CheckedInBy, by));

        ticket = await GetByIdAsync(id);
        if (updated == 1)
            return (CheckInStatus.CheckedIn, ticket);
        return ticket!.Canceled ? (CheckInStatus.Canceled, ticket) : (CheckInStatus.AlreadyUsed, ticket);
    }

    public async Task UpdateAsync(FmEventTicket ticket) => await _tickets.UpdateAsync(ticket, false);

    /// <summary>Zneplatní (nebo obnoví) všechny vstupenky objednávky.</summary>
    public async Task<int> SetOrderCanceledAsync(Order order, bool canceled, string reason)
    {
        var tickets = (await GetByOrderAsync(order.Id)).Where(t => t.Canceled != canceled).ToList();
        foreach (var t in tickets)
        {
            t.Canceled = canceled;
            await _tickets.UpdateAsync(t, false);
        }

        if (tickets.Count > 0)
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = $"{(canceled ? "Zneplatněno" : "Obnoveno")} vstupenek: {tickets.Count}. {reason}",
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });
        return tickets.Count;
    }

    /// <summary>ID objednávek, které mají aspoň jednu platnou vstupenku (případně na danou akci).</summary>
    public async Task<IList<int>> GetOrderIdsWithValidTicketsAsync(int eventId = 0)
        => await _tickets.Table.Where(t => !t.Canceled && (eventId <= 0 || t.EventId == eventId))
            .Select(t => t.OrderId).Distinct().OrderBy(id => id).ToListAsync();

    #endregion

    #region PDF a e-mail

    private void EnsureFont()
    {
        var path = _fileProvider.MapPath("~/App_Data/Pdf/FreeSerif.ttf");
        TicketPdfComposer.EnsureFont(_fileProvider.FileExists(path) ? path : null);
    }

    public async Task<byte[]> BuildPdfAsync(Order order, IList<FmEventTicket> tickets)
    {
        EnsureFont();
        var infos = await GetEventInfosAsync();
        var storeUrl = await GetStoreUrlAsync(order.StoreId);
        var store = await _storeService.GetStoreByIdAsync(order.StoreId);
        var productNames = new Dictionary<int, string>();
        foreach (var productId in tickets.Select(t => t.ProductId).Distinct())
            productNames[productId] = (await _productService.GetProductByIdAsync(productId))?.Name ?? string.Empty;

        var data = new TicketPdfData
        {
            StoreName = store?.Name ?? string.Empty,
            OrderNumber = order.CustomOrderNumber,
            Tickets = tickets.Select(t => new TicketPdfItem
            {
                EventName = InfoFor(infos, t.EventId).Name,
                IsVip = InfoFor(infos, t.EventId).IsVip(t.ProductId),
                When = InfoFor(infos, t.EventId).When,
                Note = InfoFor(infos, t.EventId).Note,
                Number = t.Number,
                Count = tickets.Count,
                HolderName = t.HolderName,
                ProductName = productNames.TryGetValue(t.ProductId, out var n) ? n : string.Empty,
                CodeFormatted = FormatCode(t.Code),
                QrPng = QrPng(TicketUrl(storeUrl, t.Code), 20)
            }).ToList()
        };
        return TicketPdfComposer.Compose(data);
    }

    /// <summary>
    /// Pošle zákazníkovi e-mail se všemi platnými vstupenkami objednávky v PDF.
    /// Vlastní předmět může obsahovat {objednavka} (číslo objednávky) a {akce} (název akce).
    /// </summary>
    public async Task<bool> SendTicketsEmailAsync(Order order, string? subject = null)
    {
        await EmailLock.WaitAsync();
        try
        {
            return await SendTicketsEmailCoreAsync(order, OneLine(subject, 200));
        }
        finally
        {
            EmailLock.Release();
        }
    }

    /// <summary>
    /// Smaže PDF přílohy už odeslaných e-mailů se vstupenkami (tabulka Download). Každé odeslání vstupenek
    /// ukládá PDF do databáze a nopCommerce ho po odeslání nemaže, databáze by jinak jen rostla.
    /// Nechává přílohy e-mailů, které čekají na odeslání (30 dnů) nebo odešly v posledních 7 dnech.
    /// PDF si zákazník kdykoli stáhne znovu ze stránky vstupenek, vzniká tam vždy nové.
    /// </summary>
    public async Task<int> CleanupSentPdfsAsync()
    {
        await EmailLock.WaitAsync();
        try
        {
            var ids = await _downloads.Table
                .Where(d => !d.UseDownloadUrl && d.Extension == ".pdf" && d.Filename.StartsWith("vstupenky-"))
                .Select(d => d.Id).ToListAsync();
            if (ids.Count == 0)
                return 0;

            var sentAfter = DateTime.UtcNow.AddDays(-7);
            var waitingAfter = DateTime.UtcNow.AddDays(-30);
            var keep = (await _queuedEmails.Table
                .Where(q => q.AttachedDownloadId > 0
                            && ((q.SentOnUtc == null && q.CreatedOnUtc > waitingAfter) || q.SentOnUtc > sentAfter))
                .Select(q => q.AttachedDownloadId).ToListAsync()).ToHashSet();
            // Pojistka: soubor ke stažení přiřazený produktu se nikdy nemaže, i kdyby se jmenoval stejně.
            foreach (var p in await _products.Table.Where(p => p.DownloadId > 0 || p.SampleDownloadId > 0)
                         .Select(p => new { p.DownloadId, p.SampleDownloadId }).ToListAsync())
            {
                keep.Add(p.DownloadId);
                keep.Add(p.SampleDownloadId);
            }

            var deleted = 0;
            foreach (var chunk in ids.Where(id => !keep.Contains(id)).Chunk(200))
                deleted += await _downloads.DeleteAsync(d => chunk.Contains(d.Id));
            return deleted;
        }
        finally
        {
            EmailLock.Release();
        }
    }

    private async Task<bool> SendTicketsEmailCoreAsync(Order order, string? subject)
    {
        var tickets = (await GetByOrderAsync(order.Id)).Where(t => !t.Canceled).ToList();
        if (tickets.Count == 0)
            return false;

        var to = tickets.First().HolderEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            await _logger.WarningAsync($"Fermatigo vstupenky: objednávka {order.CustomOrderNumber} nemá e-mail, vstupenky nelze poslat.");
            return false;
        }

        var account = await _emailAccountService.GetEmailAccountByIdAsync(_emailAccountSettings.DefaultEmailAccountId)
                      ?? (await _emailAccountService.GetAllEmailAccountsAsync()).FirstOrDefault();
        if (account == null)
        {
            await _logger.WarningAsync("Fermatigo vstupenky: v obchodě není žádný e-mailový účet, vstupenky nelze poslat.");
            return false;
        }

        var infos = await GetEventInfosAsync();
        var orderEvents = tickets.Select(t => t.EventId).Distinct().Select(id => InfoFor(infos, id)).ToList();
        var storeUrl = await GetStoreUrlAsync(order.StoreId);
        var store = await _storeService.GetStoreByIdAsync(order.StoreId);
        var pdf = await BuildPdfAsync(order, tickets);

        var download = new Download
        {
            DownloadGuid = Guid.NewGuid(),
            UseDownloadUrl = false,
            DownloadUrl = string.Empty,
            DownloadBinary = pdf,
            ContentType = "application/pdf",
            Filename = $"vstupenky-{order.CustomOrderNumber}",
            Extension = ".pdf",
            IsNew = false
        };
        await _downloadService.InsertDownloadAsync(download);

        // Objednávka může mít vstupenky na více akcí, pak se v e-mailu vypíšou všechny.
        var eventName = string.Join(", ", orderEvents.Select(e => e.Name));
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var when = orderEvents.Count == 1 ? orderEvents[0].When : string.Empty;
        var link = OrderTicketsUrl(storeUrl, order.OrderGuid);
        var noun = tickets.Count == 1 ? "vstupenku" : tickets.Count < 5 ? "vstupenky" : "vstupenek";

        var body = new StringBuilder();
        body.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#001B54;max-width:600px\">");
        body.Append($"<h2 style=\"margin:0 0 12px;font-size:22px\">Vaše vstupenky: {E(eventName)}</h2>");
        body.Append("<p>Dobrý den,</p>");
        if (orderEvents.Count == 1)
        {
            body.Append($"<p>děkujeme za nákup. Posíláme Vám {tickets.Count} {noun} na akci <strong>{E(eventName)}</strong>");
            body.Append(when.Length > 0 ? $" ({E(when)}).</p>" : ".</p>");
        }
        else
        {
            body.Append($"<p>děkujeme za nákup. Posíláme Vám {tickets.Count} {noun} na tyto akce:</p><ul>");
            foreach (var ev in orderEvents)
            {
                var n = tickets.Count(t => InfoFor(infos, t.EventId).Id == ev.Id);
                body.Append($"<li><strong>{E(ev.Name)}</strong>{(ev.When.Length > 0 ? ", " + E(ev.When) : "")} ({n}×)</li>");
            }
            body.Append("</ul>");
        }
        body.Append("<p>Vstupenky najdete v PDF v příloze tohoto e-mailu. Každá vstupenka má vlastní QR kód, který u vstupu načte obsluha. ");
        body.Append("Vstupenku můžete vytisknout, nebo ji ukázat v mobilu.</p>");
        body.Append($"<p style=\"margin:22px 0\"><a href=\"{E(link)}\" style=\"display:inline-block;padding:13px 22px;border-radius:999px;background:#A6CE38;color:#001B54;font-weight:bold;text-decoration:none\">Zobrazit a stáhnout vstupenky</a></p>");
        body.Append("<p>Na stránce se vstupenkami si můžete stáhnout i každý QR kód zvlášť jako obrázek do telefonu.</p>");
        body.Append("<p style=\"font-size:13px;color:#4a5a7a\">Každý QR kód platí pro jeden vstup. Vstupenky proto nikomu neposílejte ani je nezveřejňujte.</p>");
        body.Append($"<p>Těšíme se na Vás.<br>{E(store?.Name)}</p>");
        body.Append("</div>");

        await _queuedEmailService.InsertQueuedEmailAsync(new QueuedEmail
        {
            Priority = QueuedEmailPriority.High,
            From = account.Email,
            FromName = account.DisplayName,
            To = to,
            ToName = tickets.First().HolderName,
            Subject = string.IsNullOrWhiteSpace(subject)
                ? (orderEvents.Count == 1 ? $"Vstupenky: {eventName}, objednávka {order.CustomOrderNumber}" : $"Vstupenky, objednávka {order.CustomOrderNumber}")
                : subject.Trim()
                    .Replace("{objednavka}", order.CustomOrderNumber, StringComparison.OrdinalIgnoreCase)
                    .Replace("{objednávka}", order.CustomOrderNumber, StringComparison.OrdinalIgnoreCase)
                    .Replace("{akce}", eventName, StringComparison.OrdinalIgnoreCase),
            Body = body.ToString(),
            CreatedOnUtc = DateTime.UtcNow,
            EmailAccountId = account.Id,
            AttachedDownloadId = download.Id
        });

        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = $"E-mail se vstupenkami ({tickets.Count}) zařazen k odeslání na {to}.",
            DisplayToCustomer = false,
            CreatedOnUtc = DateTime.UtcNow
        });
        return true;
    }

    #endregion
}
