using Fermatigo.EventTickets.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;

namespace Fermatigo.EventTickets.Infrastructure;

public class PluginNopStartup : INopStartup
{
    public int Order => 3000;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EventTicketService>();
        services.AddScoped<TicketBackupService>();
    }

    public void Configure(IApplicationBuilder application) { }
}
