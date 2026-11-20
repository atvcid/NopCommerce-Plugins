using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Events;
using Nop.Services.Logging;

namespace Fermatigo.EventTickets.Services;

/// <summary>Po zaplacení vytvoří a pošle vstupenky, po stornu nebo vrácení peněz je zneplatní.</summary>
public class OrderEventConsumer :
    IConsumer<OrderPaidEvent>,
    IConsumer<OrderRefundedEvent>,
    IConsumer<OrderVoidedEvent>,
    IConsumer<OrderStatusChangedEvent>
{
    private readonly EventTicketService _service;
    private readonly ILogger _logger;

    public OrderEventConsumer(EventTicketService service, ILogger logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task HandleEventAsync(OrderPaidEvent eventMessage)
    {
        var order = eventMessage.Order;
        try
        {
            var created = await _service.CreateTicketsAsync(order);
            if (created.Count == 0)
                return;

            var settings = await _service.GetSettingsAsync();
            if (settings.SendEmail)
                await _service.SendTicketsEmailAsync(order);
        }
        catch (Exception ex)
        {
            // Chyba vstupenek nesmí zastavit zpracování platby, objednávku jde doplnit ručně v nastavení pluginu.
            await _logger.ErrorAsync($"Fermatigo vstupenky: objednávka {order.CustomOrderNumber}, vstupenky se nepodařilo vytvořit nebo poslat.", ex);
        }
    }

    public async Task HandleEventAsync(OrderRefundedEvent eventMessage)
    {
        // Jen plné vrácení peněz, částečné vrácení vstupenky nechává platné.
        if (eventMessage.Order.PaymentStatus == PaymentStatus.Refunded)
            await _service.SetOrderCanceledAsync(eventMessage.Order, true, "Objednávka byla vrácena.");
    }

    public async Task HandleEventAsync(OrderVoidedEvent eventMessage)
        => await _service.SetOrderCanceledAsync(eventMessage.Order, true, "Platba byla zrušena.");

    public async Task HandleEventAsync(OrderStatusChangedEvent eventMessage)
    {
        if (eventMessage.Order.OrderStatus == OrderStatus.Cancelled)
            await _service.SetOrderCanceledAsync(eventMessage.Order, true, "Objednávka byla stornována.");
    }
}
