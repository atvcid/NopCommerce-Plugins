using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Fermatigo.EventTickets.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public int Priority => 1000;

    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        foreach (var action in new[] { "Configure", "Ticket", "Order", "Export" })
            endpointRouteBuilder.MapControllerRoute(
                name: "Plugin.Fermatigo.EventTickets." + action,
                pattern: EventTicketsDefaults.AdminRoutePrefix + action,
                defaults: new { controller = "FmTickets", action, area = AreaNames.ADMIN });
    }
}
