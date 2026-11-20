using System.Text;
using Fermatigo.EventTickets.Models;
using Fermatigo.EventTickets.Services;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Fermatigo.EventTickets.Domain;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core.Domain.Payments;
using Nop.Services.Catalog;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.ScheduleTasks;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Fermatigo.EventTickets.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
[AutoValidateAntiforgeryToken]
public class FmTicketsController : BasePluginController
{
    private const string ViewPath = "~/Plugins/Fermatigo.EventTickets/Views/Admin/Configure.cshtml";
    private const int PageSize = 50;

    private readonly EventTicketService _service;
    private readonly ISettingService _settingService;
    private readonly IPermissionService _permissionService;
    private readonly INotificationService _notificationService;
    private readonly IProductService _productService;
    private readonly IOrderService _orderService;
    private readonly ICustomerService _customerService;
    private readonly TicketBackupService _backup;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly ILogger _logger;
    private readonly IWorkContext _workContext;

    public FmTicketsController(
        EventTicketService service,
        ISettingService settingService,
        IPermissionService permissionService,
        INotificationService notificationService,
        IProductService productService,
        IOrderService orderService,
        ICustomerService customerService,
        TicketBackupService backup,
        IScheduleTaskService scheduleTaskService,
        ILogger logger,
        IWorkContext workContext)
    {
        _workContext = workContext;
        _logger = logger;
        _backup = backup;
        _scheduleTaskService = scheduleTaskService;
        _service = service;
        _settingService = settingService;
        _permissionService = permissionService;
        _notificationService = notificationService;
        _productService = productService;
        _orderService = orderService;
        _customerService = customerService;
    }

    private Task<bool> AllowedAsync()
        => _permissionService.AuthorizeAsync(StandardPermission.Configuration.MANAGE_PLUGINS);

    private async Task<string> ProductLabelAsync(int id)
    {
        var product = await _productService.GetProductByIdAsync(id);
        return product == null || product.Deleted ? $"{id}: produkt neexistuje" : $"{id}: {product.Name}";
    }

    private async Task<ConfigurationModel> BuildModelAsync(string? q, int eventId, int page, int? editEvent)
    {
        await _service.EnsureEventsMigratedAsync();
        var settings = await _service.GetSettingsAsync();
        if (string.IsNullOrEmpty(settings.GoogleSheetSecret))
        {
            settings.GoogleSheetSecret = TicketBackupService.NewSecret();
            await _settingService.SaveSettingAsync(settings);
        }
        var stats = await _service.GetStatsAsync();
        var task = await _scheduleTaskService.GetTaskByTypeAsync(EventTicketsDefaults.BackupTaskType);
        var role = await _customerService.GetCustomerRoleBySystemNameAsync(EventTicketsDefaults.CheckInRoleSystemName);
        var model = new ConfigurationModel
        {
            SendEmail = settings.SendEmail,
            EventId = Math.Max(0, eventId),
            GoogleSheetUrl = settings.GoogleSheetUrl,
            AppsScript = TicketBackupService.AppsScript(settings.GoogleSheetSecret),
            GoogleSheetSecret = settings.GoogleSheetSecret,
            BackupLastSuccess = DateTime.TryParse(settings.BackupLastSuccessUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastUtc)
                ? EventTicketService.ToPrague(lastUtc).ToString("d. M. yyyy H:mm:ss", CultureInfo.InvariantCulture)
                : null,
            BackupLastError = settings.BackupLastError,
            BackupTaskEnabled = task?.Enabled == true,
            BackupIntervalMinutes = Math.Max(1, settings.BackupIntervalMinutes),
            CheckInRoleId = role?.Id ?? 0,
            Total = stats.Total,
            CheckedIn = stats.CheckedIn,
            Canceled = stats.Canceled,
            Search = q ?? string.Empty,
            Page = Math.Max(0, page)
        };

        var events = await _service.GetEventsAsync();
        foreach (var e in events)
        {
            var info = EventInfo.From(e);
            var s = await _service.GetStatsAsync(e.Id);
            var row = new EventRowModel
            {
                Id = e.Id,
                Name = info.Name,
                When = info.When,
                Total = s.Total,
                CheckedIn = s.CheckedIn,
                Canceled = s.Canceled,
                TicketCount = await _service.CountTicketsAsync(e.Id)
            };
            var vip = EventTicketService.ParseProductIds(e.VipProductIds);
            foreach (var id in EventTicketService.ParseProductIds(e.ProductIds))
                row.Products.Add((vip.Contains(id) ? "VIP · " : "") + await ProductLabelAsync(id));
            model.Events.Add(row);
        }

        if (editEvent.HasValue)
        {
            var e = await _service.GetEventByIdAsync(editEvent.Value);
            model.EditEvent = e == null
                ? new EventEditModel()
                : new EventEditModel
                {
                    Id = e.Id,
                    Name = e.Name,
                    EventDate = e.EventDate,
                    EventPlace = e.EventPlace,
                    EventNote = e.EventNote,
                    ProductIds = string.Join(",", EventTicketService.ParseProductIds(e.ProductIds)
                        .Except(EventTicketService.ParseProductIds(e.VipProductIds))),
                    VipProductIds = e.VipProductIds
                };
            await FillProductNamesAsync(model.EditEvent);
        }

        var infos = await _service.GetEventInfosAsync();
        var (rows, total) = await _service.SearchAsync(q, model.EventId, model.Page, PageSize);
        model.Found = total;
        model.PageCount = (total + PageSize - 1) / PageSize;
        var storeUrl = await _service.GetStoreUrlAsync(0);
        var orders = new Dictionary<int, (string, bool)>();
        foreach (var t in rows)
        {
            var (number, orderCanceled) = await _service.OrderInfoAsync(t.OrderId, orders);
            model.Tickets.Add(new TicketRowModel
            {
                OrderCanceled = orderCanceled,
                EventName = EventTicketService.InfoFor(infos, t.EventId).Name,
                IsVip = EventTicketService.InfoFor(infos, t.EventId).IsVip(t.ProductId),
                Id = t.Id,
                Code = EventTicketService.FormatCode(t.Code),
                OrderId = t.OrderId,
                OrderNumber = number,
                Number = t.Number,
                HolderName = t.HolderName,
                HolderEmail = t.HolderEmail,
                CheckedIn = t.CheckedInOnUtc.HasValue ? EventTicketService.ToPrague(t.CheckedInOnUtc.Value).ToString("d. M. yyyy H:mm") : null,
                CheckedInBy = t.CheckedInBy,
                Canceled = t.Canceled,
                Url = EventTicketService.TicketUrl(storeUrl, t.Code)
            });
        }
        return model;
    }

    public async Task<IActionResult> Configure(string? q, int eventId = 0, int page = 0, int? editEvent = null, bool restore = false)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();
        var model = await BuildModelAsync(q, eventId, page, editEvent);
        if (restore)
            model.Restore = await BuildRestoreAsync();
        return View(ViewPath, model);
    }

    /// <summary>Porovnání archivu v Google Tabulce s e-shopem: které vstupenky v e-shopu chybí.</summary>
    private async Task<RestoreModel> BuildRestoreAsync()
    {
        var restore = new RestoreModel();
        var (archive, error) = await _backup.ReadArchiveAsync();
        if (archive == null)
        {
            restore.Error = error;
            return restore;
        }

        var existing = await _service.GetAllCodesAsync();
        var missing = archive.Tickets.Where(t => !existing.Contains(t.Code)).ToList();
        var deleted = await _service.GetManuallyDeletedCodesAsync(missing.Select(t => t.OrderId));
        restore.InArchive = archive.Tickets.Count;
        restore.InShop = archive.Tickets.Count - missing.Count;
        foreach (var t in missing.OrderBy(t => t.EventName).ThenBy(t => t.OrderId).ThenBy(t => t.Number))
            restore.Rows.Add(new RestoreRowModel
            {
                Code = t.Code,
                CodeFormatted = EventTicketService.FormatCode(t.Code),
                EventName = t.EventName,
                OrderNumber = t.OrderNumber,
                OrderExists = await _orderService.GetOrderByIdAsync(t.OrderId) != null,
                Number = t.Number,
                HolderName = t.HolderName,
                HolderEmail = t.HolderEmail,
                State = t.State,
                LastSeen = t.LastSeen,
                ManuallyDeleted = deleted.Contains(t.Code)
            });
        return restore;
    }

    /// <summary>Obnoví vybrané vstupenky z archivu v Google Tabulce. Existující nikdy nepřepíše.</summary>
    [HttpPost]
    public async Task<IActionResult> Restore(string[]? codes)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var selected = (codes ?? Array.Empty<string>()).Select(EventTicketService.NormalizeCode).Where(c => c.Length > 0).ToHashSet();
        if (selected.Count == 0)
        {
            _notificationService.WarningNotification("Nevybrali jste žádnou vstupenku k obnovení.");
            return RedirectToAction("Configure", new { restore = true });
        }

        var (archive, error) = await _backup.ReadArchiveAsync();
        if (archive == null)
        {
            _notificationService.ErrorNotification(error);
            return RedirectToAction("Configure", new { restore = true });
        }

        var who = await WhoAsync();
        var count = await _service.RestoreFromArchiveAsync(archive, selected, who);
        await _logger.InformationAsync($"Fermatigo vstupenky: obnoveno z Google Tabulky {count} vstupenek ({string.Join(", ", selected.Take(200))}). Obnovil: {who}.");
        _notificationService.SuccessNotification($"Obnoveno vstupenek: {count}. Mají původní kódy, jejich QR kódy znovu platí.");
        return RedirectToAction("Configure");
    }

    [HttpPost, ActionName("Configure")]
    public async Task<IActionResult> ConfigurePost(ConfigurationModel model)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var settings = await _service.GetSettingsAsync();
        settings.SendEmail = model.SendEmail;
        await _settingService.SaveSettingAsync(settings);

        _notificationService.SuccessNotification("Nastavení vstupenek je uložené.");
        return RedirectToAction("Configure");
    }

    /// <summary>Založení nebo úprava akce. Produkt smí patřit jen jedné akci.</summary>
    [HttpPost]
    public async Task<IActionResult> EventSave(EventEditModel model)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var name = (model.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            _notificationService.ErrorNotification("Vyplňte název akce.");
            return await EventFormAgainAsync(model);
        }

        // VIP produkty jsou také produkty akce, do ProductIds se přidají samy.
        var vipIds = EventTicketService.ParseProductIds(model.VipProductIds);
        var productIds = EventTicketService.ParseProductIds(model.ProductIds).Union(vipIds).ToList();
        foreach (var other in (await _service.GetEventsAsync()).Where(e => e.Id != model.Id))
        {
            var shared = EventTicketService.ParseProductIds(other.ProductIds).Intersect(productIds).ToList();
            if (shared.Count > 0)
            {
                _notificationService.ErrorNotification(
                    $"Produkt {string.Join(", ", shared)} už je vstupenkou akce „{other.Name}“. Jeden produkt může patřit jen jedné akci, pro novou akci založte nový produkt.");
                return await EventFormAgainAsync(model);
            }
        }

        var e = model.Id > 0 ? await _service.GetEventByIdAsync(model.Id) : null;
        var isNew = e == null;
        e ??= new FmEvent { CreatedOnUtc = DateTime.UtcNow };
        e.Name = name;
        e.EventDate = (model.EventDate ?? string.Empty).Trim();
        e.EventPlace = (model.EventPlace ?? string.Empty).Trim();
        e.EventNote = (model.EventNote ?? string.Empty).Trim();
        e.ProductIds = string.Join(",", productIds);
        e.VipProductIds = string.Join(",", vipIds);
        if (isNew)
            await _service.InsertEventAsync(e);
        else
            await _service.UpdateEventAsync(e);

        _notificationService.SuccessNotification(isNew ? $"Akce „{name}“ je založená." : $"Akce „{name}“ je uložená.");
        if (productIds.Count == 0)
            _notificationService.WarningNotification("Akce zatím nemá žádný produkt vstupenky, vstupenky se k ní nevytvoří.");
        return RedirectToAction("Configure");
    }

    /// <summary>Formulář akce znovu s tím, co uživatel vyplnil, aby po chybě nemusel psát vše znovu.</summary>
    private async Task<IActionResult> EventFormAgainAsync(EventEditModel posted)
    {
        var page = await BuildModelAsync(null, 0, 0, null);
        page.EditEvent = posted with { ProductNames = new List<string>() };
        await FillProductNamesAsync(page.EditEvent);
        return View(ViewPath, page);
    }

    private async Task FillProductNamesAsync(EventEditModel m)
    {
        foreach (var id in EventTicketService.ParseProductIds(m.ProductIds))
            m.ProductNames.Add(await ProductLabelAsync(id));
        foreach (var id in EventTicketService.ParseProductIds(m.VipProductIds))
            m.ProductNames.Add("VIP · " + await ProductLabelAsync(id));
    }

    /// <summary>Důvod smazání: oříznutý, jeden řádek, nejvýš 1000 znaků.</summary>
    private static string CleanReason(string? reason)
    {
        var r = string.Join(" ", (reason ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (r.EndsWith('.'))
            r = r.TrimEnd('.');
        r = r.Length > 1000 ? r[..1000] : r;
        return r.Length == 0 ? r : r + ".";
    }

    private async Task<string> WhoAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer.Email ?? $"zákazník {customer.Id}";
    }

    /// <summary>
    /// Smazání akce. Akci se vstupenkami smaže i s jejími vstupenkami, ale jen když uživatel
    /// pro potvrzení opíše název akce (kontroluje se i tady na serveru).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> EventDelete(int id, string? confirmName, string? reason)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var e = await _service.GetEventByIdAsync(id);
        if (e == null)
            return RedirectToAction("Configure");

        var tickets = await _service.GetByEventAsync(id);
        if (tickets.Count > 0 && !string.Equals((confirmName ?? string.Empty).Trim(), e.Name.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            _notificationService.ErrorNotification($"Akce „{e.Name}“ se nesmazala: pro potvrzení je potřeba opsat přesný název akce.");
            return RedirectToAction("Configure");
        }
        var why = CleanReason(reason);
        if (tickets.Count > 0 && why.Length == 0)
        {
            _notificationService.ErrorNotification($"Akce „{e.Name}“ se nesmazala: chybí důvod smazání.");
            return RedirectToAction("Configure");
        }

        var who = await WhoAsync();
        if (tickets.Count > 0)
            await _service.DeleteTicketsAsync(tickets, who, $"Smazána akce „{e.Name}“. Důvod: {why}");
        await _service.DeleteEventAsync(e);
        await _logger.InformationAsync($"Fermatigo vstupenky: smazána akce „{e.Name}“ (ID {e.Id}) i se vstupenkami ({tickets.Count}). Smazal: {who}.{(why.Length > 0 ? " Důvod: " + why : "")}");
        _notificationService.SuccessNotification(tickets.Count > 0
            ? $"Akce „{e.Name}“ je smazaná i s {tickets.Count} {(tickets.Count == 1 ? "vstupenkou" : "vstupenkami")}."
            : $"Akce „{e.Name}“ je smazaná.");
        return RedirectToAction("Configure");
    }

    /// <summary>Ruční změna jedné vstupenky: odbavit, vrátit odbavení, zneplatnit, obnovit.</summary>
    [HttpPost]
    public async Task<IActionResult> Ticket(int id, string op, string? q, string? reason, int eventId = 0, int page = 0)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var ticket = await _service.GetByIdAsync(id);
        if (ticket == null)
        {
            _notificationService.ErrorNotification("Vstupenka nebyla nalezena.");
            return RedirectToAction("Configure", new { q, eventId, page });
        }

        switch (op)
        {
            case "checkin":
                var (status, _) = await _service.CheckInAsync(ticket.Code, "administrace");
                if (status == CheckInStatus.CheckedIn)
                    _notificationService.SuccessNotification($"Vstupenka {ticket.Number} je odbavená.");
                else
                    _notificationService.WarningNotification("Vstupenku nelze odbavit, je už použitá nebo neplatná.");
                break;
            case "undo":
                ticket.CheckedInOnUtc = null;
                ticket.CheckedInBy = string.Empty;
                await _service.UpdateAsync(ticket);
                _notificationService.SuccessNotification($"Odbavení vstupenky {ticket.Number} je zrušené, vstupenka znovu platí.");
                break;
            case "cancel":
                ticket.Canceled = true;
                await _service.UpdateAsync(ticket);
                _notificationService.SuccessNotification($"Vstupenka {ticket.Number} je zneplatněná.");
                break;
            case "delete":
                var why = CleanReason(reason);
                if (why.Length == 0)
                {
                    _notificationService.ErrorNotification($"Vstupenka {ticket.Number} se nesmazala: chybí důvod smazání.");
                    break;
                }
                var who = await WhoAsync();
                await _service.DeleteTicketsAsync(new List<FmEventTicket> { ticket }, who, $"Ruční smazání v administraci. Důvod: {why}");
                await _logger.InformationAsync($"Fermatigo vstupenky: smazána vstupenka č. {ticket.Number}, kód {EventTicketService.FormatCode(ticket.Code)}, objednávka ID {ticket.OrderId}. Smazal: {who}. Důvod: {why}");
                _notificationService.SuccessNotification($"Vstupenka {ticket.Number} ({EventTicketService.FormatCode(ticket.Code)}) je smazaná.");
                break;
            case "restore":
                ticket.Canceled = false;
                await _service.UpdateAsync(ticket);
                _notificationService.SuccessNotification($"Vstupenka {ticket.Number} znovu platí.");
                break;
        }
        return RedirectToAction("Configure", new { q, eventId, page });
    }

    /// <summary>Akce s celou objednávkou: doplnit vstupenky, poslat znovu e-mail.</summary>
    [HttpPost]
    public async Task<IActionResult> Order(string orderNumber, string op, string? subject)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var text = (orderNumber ?? string.Empty).Trim();
        var order = await _orderService.GetOrderByCustomOrderNumberAsync(text);
        if (order == null && int.TryParse(text, out var orderId))
            order = await _orderService.GetOrderByIdAsync(orderId);
        if (order == null || order.Deleted)
        {
            _notificationService.ErrorNotification($"Objednávka „{text}“ neexistuje.");
            return RedirectToAction("Configure");
        }

        if (op == "generate")
        {
            if (order.PaymentStatus != PaymentStatus.Paid || order.OrderStatus == OrderStatus.Cancelled)
            {
                _notificationService.WarningNotification($"Objednávka {order.CustomOrderNumber} není zaplacená, vstupenky se vytvoří až po zaplacení.");
                return RedirectToAction("Configure", new { q = order.CustomOrderNumber });
            }
            var created = await _service.CreateTicketsAsync(order);
            if (created.Count == 0)
            {
                _notificationService.WarningNotification($"Objednávka {order.CustomOrderNumber}: žádné nové vstupenky. Buď už vytvořené jsou, nebo objednávka neobsahuje produkt nastavený jako vstupenka.");
                return RedirectToAction("Configure", new { q = order.CustomOrderNumber });
            }
            var sent = (await _service.GetSettingsAsync()).SendEmail && await _service.SendTicketsEmailAsync(order);
            _notificationService.SuccessNotification($"Objednávka {order.CustomOrderNumber}: vytvořeno vstupenek {created.Count}{(sent ? ", e-mail zařazen k odeslání" : "")}.");
        }
        else if (op == "resend")
        {
            if (await _service.SendTicketsEmailAsync(order, subject))
                _notificationService.SuccessNotification($"E-mail se vstupenkami k objednávce {order.CustomOrderNumber} je zařazený k odeslání.");
            else
                _notificationService.WarningNotification($"Objednávka {order.CustomOrderNumber} nemá platné vstupenky nebo e-mail, nic se neposlalo. Podrobnosti najdete v protokolu.");
        }
        return RedirectToAction("Configure", new { q = order.CustomOrderNumber });
    }

    /// <summary>Znovu poslat vstupenky všem: jeden e-mail na objednávku s platnými vstupenkami.</summary>
    [HttpPost]
    public async Task<IActionResult> ResendAll(string? subject, int eventId = 0)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        await ResendOrdersAsync(await _service.GetOrderIdsWithValidTicketsAsync(eventId), subject);
        return RedirectToAction("Configure");
    }

    /// <summary>
    /// Znovu poslat jen vybraným: zaškrtnuté vstupenky se seskupí podle objednávky a každá objednávka
    /// dostane jeden e-mail se všemi svými platnými vstupenkami.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ResendSelected(int[]? ids, string? subject, string? q, int eventId = 0, int page = 0)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var orderIds = new List<int>();
        foreach (var id in (ids ?? Array.Empty<int>()).Distinct())
        {
            var ticket = await _service.GetByIdAsync(id);
            if (ticket != null && !ticket.Canceled && !orderIds.Contains(ticket.OrderId))
                orderIds.Add(ticket.OrderId);
        }

        if (orderIds.Count == 0)
            _notificationService.WarningNotification("Nevybrali jste žádnou platnou vstupenku, nic se neposlalo.");
        else
            await ResendOrdersAsync(orderIds, subject);
        return RedirectToAction("Configure", new { q, eventId, page });
    }

    private async Task ResendOrdersAsync(IEnumerable<int> orderIds, string? subject)
    {
        int sent = 0, failed = 0;
        foreach (var orderId in orderIds)
        {
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null || order.Deleted || order.OrderStatus == OrderStatus.Cancelled)
                continue;
            try
            {
                if (await _service.SendTicketsEmailAsync(order, subject))
                    sent++;
                else
                    failed++;
            }
            catch (Exception ex)
            {
                failed++;
                await _logger.ErrorAsync($"Fermatigo vstupenky: hromadné odeslání, objednávka {order.CustomOrderNumber} selhala.", ex);
            }
        }

        if (sent > 0)
            _notificationService.SuccessNotification($"E-mail se vstupenkami je zařazený k odeslání u {sent} {(sent == 1 ? "objednávky" : "objednávek")}. Odejde přes frontu zpráv.");
        if (failed > 0)
            _notificationService.WarningNotification($"U {failed} {(failed == 1 ? "objednávky" : "objednávek")} se e-mail poslat nepodařilo (chybí e-mail nebo e-mailový účet). Podrobnosti najdete v protokolu.");
        if (sent == 0 && failed == 0)
            _notificationService.WarningNotification("Žádná objednávka nemá platné vstupenky, nic se neposlalo.");
    }

    /// <summary>Záloha do Google Tabulky: uložit adresu skriptu (a hned vyzkoušet) nebo zálohovat teď.</summary>
    [HttpPost]
    public async Task<IActionResult> Backup(string? googleSheetUrl, string? googleSheetSecret, string op, int backupInterval = 0)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        if (op == "interval")
        {
            if (!EventTicketsDefaults.BackupIntervals.Any(i => i.Minutes == backupInterval))
            {
                _notificationService.ErrorNotification("Zvolte interval zálohy ze seznamu.");
                return RedirectToAction("Configure");
            }
            var s = await _service.GetSettingsAsync();
            s.BackupIntervalMinutes = backupInterval;
            await _settingService.SaveSettingAsync(s);
            // interval se zapíše i do sekund plánované úlohy (Systém → Plánované úlohy)
            await EnsureBackupTaskAsync();
            _notificationService.SuccessNotification("Interval zálohy uložen: " + EventTicketsDefaults.BackupIntervalLabel(backupInterval)
                + ". Zapsaný je i v plánovaných úlohách, nopCommerce ho převezme po restartu aplikace (Restartovat aplikaci nahoře v administraci).");
            return RedirectToAction("Configure");
        }

        if (op == "save")
        {
            var url = (googleSheetUrl ?? string.Empty).Trim();
            if (url.Length > 0 && !TicketBackupService.IsValidUrl(url))
            {
                _notificationService.ErrorNotification("Adresa musí být webová aplikace Google Apps Script: začíná https://script.google.com/macros/s/ a končí /exec.");
                return RedirectToAction("Configure");
            }
            var settings = await _service.GetSettingsAsync();
            settings.GoogleSheetUrl = url;
            // Tajný klíč jde přepsat klíčem ze skriptu (například po nové instalaci e-shopu nebo pluginu).
            var secret = (googleSheetSecret ?? string.Empty).Trim();
            if (secret.Length > 0 && secret != settings.GoogleSheetSecret)
            {
                if (secret.Length < 16 || !secret.All(char.IsLetterOrDigit))
                {
                    _notificationService.ErrorNotification("Tajný klíč musí mít aspoň 16 znaků, jen písmena a číslice. Opište ho ze skriptu (řádek const SECRET).");
                    return RedirectToAction("Configure");
                }
                settings.GoogleSheetSecret = secret;
            }
            settings.BackupLastHash = string.Empty;
            settings.BackupLastError = string.Empty;
            await _settingService.SaveSettingAsync(settings);
            await EnsureBackupTaskAsync();
            if (url.Length == 0)
            {
                _notificationService.SuccessNotification("Záloha do Google Tabulky je vypnutá.");
                return RedirectToAction("Configure");
            }
        }

        var result = await _backup.RunAsync(true);
        if (result.Ok)
            _notificationService.SuccessNotification(result.Message);
        else
            _notificationService.ErrorNotification(result.Message);
        return RedirectToAction("Configure");
    }

    private async Task EnsureBackupTaskAsync()
    {
        var settings = await _service.GetSettingsAsync();
        var seconds = EventTicketsDefaults.BackupTaskSeconds(settings.BackupIntervalMinutes);
        var task = await _scheduleTaskService.GetTaskByTypeAsync(EventTicketsDefaults.BackupTaskType);
        if (task != null && task.Seconds != seconds)
        {
            task.Seconds = seconds;
            await _scheduleTaskService.UpdateTaskAsync(task);
        }
        if (task == null)
            await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
            {
                Name = EventTicketsDefaults.BackupTaskName,
                Seconds = seconds,
                Type = EventTicketsDefaults.BackupTaskType,
                Enabled = true,
                LastEnabledUtc = DateTime.UtcNow,
                StopOnError = false
            });
        else if (!task.Enabled)
        {
            task.Enabled = true;
            task.LastEnabledUtc = DateTime.UtcNow;
            await _scheduleTaskService.UpdateTaskAsync(task);
        }
    }

    /// <summary>Seznam všech vstupenek do Excelu (CSV se středníkem, UTF-8).</summary>
    public async Task<IActionResult> Export(int eventId = 0)
    {
        if (!await AllowedAsync())
            return AccessDeniedView();

        var infos = await _service.GetEventInfosAsync();
        // Hodnota začínající =, +, - nebo @ by se v Excelu spustila jako vzorec (jméno zadává zákazník): dostane apostrof.
        static string Cell(string? v)
        {
            var s = v ?? string.Empty;
            if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
                s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder();
        sb.AppendLine("Akce;Objednávka;Vstupenka;VIP;Jméno;E-mail;Kód;Stav;Odbaveno;Odbavil");
        var all = (await _service.GetAllAsync()).Where(t => eventId <= 0 || t.EventId == eventId).ToList();
        var orders = await _service.OrderInfosAsync(all.Select(t => t.OrderId));
        foreach (var t in all)
        {
            var (number, orderCanceled) = await _service.OrderInfoAsync(t.OrderId, orders);
            var state = EventTicketService.StateLabel(t, orderCanceled);
            sb.AppendLine(string.Join(";",
                Cell(EventTicketService.InfoFor(infos, t.EventId).Name), Cell(number), t.Number, Cell(EventTicketService.InfoFor(infos, t.EventId).IsVip(t.ProductId) ? "ano" : "ne"), Cell(t.HolderName), Cell(t.HolderEmail), Cell(EventTicketService.FormatCode(t.Code)), Cell(state),
                Cell(t.CheckedInOnUtc.HasValue ? EventTicketService.ToPrague(t.CheckedInOnUtc.Value).ToString("d. M. yyyy H:mm:ss") : ""),
                Cell(t.CheckedInBy)));
        }
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"vstupenky-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
