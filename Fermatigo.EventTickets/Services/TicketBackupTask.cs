using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Fermatigo.EventTickets.Services;

/// <summary>
/// Plánovaná úloha nopCommerce: běží v intervalu z nastavení pluginu (výchozí jednou denně) a pošle zálohu vstupenek,
/// pokud se něco změnilo. Nejvýš jednou za 6 hodin také uklidí PDF přílohy už odeslaných e-mailů.
/// </summary>
public class TicketBackupTask : IScheduleTask
{
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(6);
    private static DateTime _lastCleanupUtc = DateTime.MinValue;

    private readonly TicketBackupService _backup;
    private readonly EventTicketService _tickets;
    private readonly ILogger _logger;

    public TicketBackupTask(TicketBackupService backup, EventTicketService tickets, ILogger logger)
    {
        _backup = backup;
        _tickets = tickets;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        if (DateTime.UtcNow - _lastCleanupUtc > CleanupEvery)
        {
            _lastCleanupUtc = DateTime.UtcNow;
            try
            {
                var deleted = await _tickets.CleanupSentPdfsAsync();
                if (deleted > 0)
                    await _logger.InformationAsync($"Fermatigo vstupenky: smazáno {deleted} PDF příloh už odeslaných e-mailů se vstupenkami.");
            }
            catch (Exception ex)
            {
                // úklid nesmí zastavit zálohu
                await _logger.WarningAsync("Fermatigo vstupenky: úklid PDF příloh se nepodařil.", ex);
            }
        }

        await _backup.RunAsync(false);
    }
}
