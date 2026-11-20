using Fermatigo.EventTickets.Models;
using Fermatigo.EventTickets.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Stores;

namespace Fermatigo.EventTickets.Controllers;

/// <summary>
/// Veřejné stránky: vstupenky zákazníka (odkaz z e-mailu) a odbavení po načtení QR kódu.
/// Obyčejný Controller, stránky jsou samostatné a nezávisí na šabloně obchodu.
/// </summary>
public class FmTicketPublicController : Controller
{
    private const string ViewTickets = "~/Plugins/Fermatigo.EventTickets/Views/Public/Tickets.cshtml";
    private const string ViewScan = "~/Plugins/Fermatigo.EventTickets/Views/Public/Scan.cshtml";

    private readonly EventTicketService _service;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly ICustomerService _customerService;
    private readonly IWorkContext _workContext;
    private readonly IStoreService _storeService;
    private readonly ILocalizationService _localizationService;

    public FmTicketPublicController(
        EventTicketService service,
        IOrderService orderService,
        IProductService productService,
        ICustomerService customerService,
        IWorkContext workContext,
        IStoreService storeService,
        ILocalizationService localizationService)
    {
        _service = service;
        _orderService = orderService;
        _productService = productService;
        _customerService = customerService;
        _workContext = workContext;
        _storeService = storeService;
        _localizationService = localizationService;
    }

    private void Private()
    {
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        Response.Headers.CacheControl = "no-store";
    }

    private async Task<EventInfoModel> EventInfoAsync(int storeId, EventInfo info)
    {
        // Bez objednávky (neznámý kód) první obchod: stránka pak vypadá stejně pro platný i neplatný kód.
        var store = await _storeService.GetStoreByIdAsync(storeId)
                    ?? (await _storeService.GetAllStoresAsync()).FirstOrDefault();
        return new EventInfoModel
        {
            StoreName = store?.Name ?? string.Empty,
            EventName = info.Name,
            EventDate = info.Date,
            EventPlace = info.Place,
            EventNote = info.Note
        };
    }

    /// <summary>
    /// Akce pro stránku bez oprávnění k údajům vstupenky: jediná akce v e-shopu, jinak obecná „Vstupenka“.
    /// Nezávisí na kódu, takže podle stránky nejde poznat, jestli kód existuje.
    /// </summary>
    private async Task<EventInfo> NeutralEventAsync()
    {
        var events = await _service.GetEventsAsync();
        return EventInfo.From(events.Count == 1 ? events[0] : null);
    }

    private async Task<string> ProductNameAsync(int productId)
        => (await _productService.GetProductByIdAsync(productId))?.Name ?? string.Empty;

    private async Task<bool> IsStaffAsync(Customer customer)
        => await _customerService.IsRegisteredAsync(customer)
           && (await _customerService.IsInCustomerRoleAsync(customer, EventTicketsDefaults.CheckInRoleSystemName)
               || await _customerService.IsAdminAsync(customer));

    [HttpGet("vstupenky/{orderGuid:guid}")]
    public async Task<IActionResult> Tickets(Guid orderGuid)
    {
        Private();
        var order = await _orderService.GetOrderByGuidAsync(orderGuid);
        if (order == null || order.Deleted)
            return NotFound();

        var tickets = await _service.GetByOrderAsync(order.Id);
        var infos = await _service.GetEventInfosAsync();
        var orderEvents = tickets.Select(t => EventTicketService.InfoFor(infos, t.EventId)).DistinctBy(e => e.Id).ToList();
        var model = new OrderTicketsModel
        {
            Event = await EventInfoAsync(order.StoreId, orderEvents.Count == 1
                ? orderEvents[0]
                : orderEvents.Count == 0 ? await NeutralEventAsync() : new EventInfo(0, "Vstupenky", "", "", "")),
            MultiEvent = orderEvents.Count > 1,
            OrderGuid = order.OrderGuid,
            OrderNumber = order.CustomOrderNumber
        };
        foreach (var t in tickets)
            model.Tickets.Add(new PublicTicketModel
            {
                EventName = EventTicketService.InfoFor(infos, t.EventId).Name,
                EventWhen = EventTicketService.InfoFor(infos, t.EventId).When,
                EventNote = EventTicketService.InfoFor(infos, t.EventId).Note,
                IsVip = EventTicketService.InfoFor(infos, t.EventId).IsVip(t.ProductId),
                Code = t.Code,
                CodeFormatted = EventTicketService.FormatCode(t.Code),
                Number = t.Number,
                Count = tickets.Count,
                HolderName = t.HolderName,
                ProductName = await ProductNameAsync(t.ProductId),
                Canceled = t.Canceled,
                CheckedIn = t.CheckedInOnUtc.HasValue
            });

        return View(ViewTickets, model);
    }

    [HttpGet("vstupenky/{orderGuid:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid orderGuid)
    {
        Private();
        var order = await _orderService.GetOrderByGuidAsync(orderGuid);
        if (order == null || order.Deleted)
            return NotFound();
        var tickets = (await _service.GetByOrderAsync(order.Id)).Where(t => !t.Canceled).ToList();
        if (tickets.Count == 0)
            return NotFound();

        var pdf = await _service.BuildPdfAsync(order, tickets);
        return File(pdf, "application/pdf", $"vstupenky-{order.CustomOrderNumber}.pdf");
    }

    /// <summary>
    /// QR kód jen přes GUID objednávky: odpověď 404 nebo obrázek by jinak prozradila,
    /// jestli krátký kód existuje, a kódy by šlo zkoušet naslepo.
    /// </summary>
    [HttpGet("vstupenky/{orderGuid:guid}/qr/{code}.png")]
    public async Task<IActionResult> Qr(Guid orderGuid, string code, bool download = false)
    {
        Private();
        var order = await _orderService.GetOrderByGuidAsync(orderGuid);
        var ticket = await _service.GetByCodeAsync(code);
        if (order == null || order.Deleted || ticket == null || ticket.OrderId != order.Id)
            return NotFound();
        var storeUrl = await _service.GetStoreUrlAsync(order?.StoreId ?? 0);
        var png = EventTicketService.QrPng(EventTicketService.TicketUrl(storeUrl, ticket.Code), 16);
        return download
            ? File(png, "image/png", $"vstupenka-{ticket.Number}-{ticket.Code[..5]}.png")
            : File(png, "image/png");
    }

    private async Task<ScanModel> ScanModelAsync(string code, CheckInStatus? result)
    {
        Private();
        var customer = await _workContext.GetCurrentCustomerAsync();
        var isStaff = await IsStaffAsync(customer);
        var ticket = await _service.GetByCodeAsync(code);
        var order = ticket == null ? null : await _orderService.GetOrderByIdAsync(ticket.OrderId);

        var showTicket = ticket != null && isStaff;
        var model = new ScanModel
        {
            Event = await EventInfoAsync(order?.StoreId ?? 0, showTicket
                ? EventTicketService.InfoFor(await _service.GetEventInfosAsync(), ticket!.EventId)
                : await NeutralEventAsync()),
            Code = EventTicketService.NormalizeCode(code),
            IsStaff = isStaff,
            IsRegistered = await _customerService.IsRegisteredAsync(customer),
            StaffName = isStaff ? (customer.Email ?? string.Empty) : string.Empty,
            LoginUrl = "/login?returnUrl=" + Uri.EscapeDataString(Request.Path),
            Result = result,
            Found = ticket != null
        };

        // Údaje o vstupence vidí jen obsluha. Kdokoli jiný (třeba zákazník, který si QR načte sám) jen obecnou stránku.
        if (ticket == null || !isStaff)
            return model;

        var count = (await _service.GetByOrderAsync(ticket.OrderId)).Count;
        model.Canceled = ticket.Canceled;
        model.CheckedInAt = ticket.CheckedInOnUtc.HasValue
            ? EventTicketService.ToPrague(ticket.CheckedInOnUtc.Value).ToString("d. M. yyyy H:mm:ss")
            : null;
        model.CheckedInBy = ticket.CheckedInBy;
        model.Number = ticket.Number;
        model.Count = count;
        model.HolderName = ticket.HolderName;
        model.HolderEmail = ticket.HolderEmail;
        model.ProductName = await ProductNameAsync(ticket.ProductId);
        // VIP vidí jen obsluha (sem se dostane jen u přihlášené obsluhy a nalezené vstupenky).
        model.IsVip = EventTicketService.InfoFor(await _service.GetEventInfosAsync(), ticket.EventId).IsVip(ticket.ProductId);
        if (order != null)
        {
            model.OrderNumber = order.CustomOrderNumber;
            model.OrderStatus = await _localizationService.GetLocalizedEnumAsync(order.OrderStatus);
            model.PaymentStatus = await _localizationService.GetLocalizedEnumAsync(order.PaymentStatus);
            model.PaymentOk = order.PaymentStatus == PaymentStatus.Paid && order.OrderStatus != OrderStatus.Cancelled;
        }
        return model;
    }

    [HttpGet("vstupenka/{code}")]
    public async Task<IActionResult> Scan(string code)
        => View(ViewScan, await ScanModelAsync(code, null));

    [HttpPost("vstupenka/{code}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(string code)
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await IsStaffAsync(customer))
            return View(ViewScan, await ScanModelAsync(code, null));

        var (status, _) = await _service.CheckInAsync(code, customer.Email ?? $"zákazník {customer.Id}");
        return View(ViewScan, await ScanModelAsync(code, status));
    }
}
