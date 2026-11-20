using Nop.Core;
using Fermatigo.EventTickets.Services;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Fermatigo.EventTickets;

public class EventTicketsPlugin : BasePlugin, IMiscPlugin
{
    private readonly ISettingService _settingService;
    private readonly ICustomerService _customerService;
    private readonly IWebHelper _webHelper;
    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly EventTicketService _ticketService;

    public EventTicketsPlugin(ISettingService settingService, ICustomerService customerService, IWebHelper webHelper,
        IScheduleTaskService scheduleTaskService, EventTicketService ticketService)
    {
        _ticketService = ticketService;
        _settingService = settingService;
        _customerService = customerService;
        _webHelper = webHelper;
        _scheduleTaskService = scheduleTaskService;
    }

    /// <summary>
    /// Tajný klíč pro Google Tabulku a plánovaná úloha zálohy. Sekundy úlohy odpovídají intervalu z nastavení pluginu
    /// (výchozí jednou denně). Běží při instalaci a aktualizaci, tedy vždy před restartem, takže se interval hned projeví.
    /// </summary>
    private async Task EnsureBackupAsync()
    {
        var settings = await _settingService.LoadSettingAsync<EventTicketsSettings>();
        var save = false;
        if (string.IsNullOrEmpty(settings.GoogleSheetSecret))
        {
            settings.GoogleSheetSecret = TicketBackupService.NewSecret();
            save = true;
        }
        if (settings.BackupIntervalMinutes < 1
            || !await _settingService.SettingExistsAsync(settings, s => s.BackupIntervalMinutes))
        {
            settings.BackupIntervalMinutes = EventTicketsDefaults.DefaultBackupIntervalMinutes;
            save = true;
        }
        if (save)
            await _settingService.SaveSettingAsync(settings);

        var seconds = EventTicketsDefaults.BackupTaskSeconds(settings.BackupIntervalMinutes);
        var task = await _scheduleTaskService.GetTaskByTypeAsync(EventTicketsDefaults.BackupTaskType);
        if (task != null)
        {
            if (task.Seconds != seconds)
            {
                task.Seconds = seconds;
                await _scheduleTaskService.UpdateTaskAsync(task);
            }
            return;
        }
        await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Name = EventTicketsDefaults.BackupTaskName,
            Seconds = seconds,
            Type = EventTicketsDefaults.BackupTaskType,
            Enabled = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError = false
        });
    }

    public override string GetConfigurationPageUrl()
        => $"{_webHelper.GetStoreLocation()}{EventTicketsDefaults.AdminRoutePrefix}Configure";

    private async Task EnsureCheckInRoleAsync()
    {
        if (await _customerService.GetCustomerRoleBySystemNameAsync(EventTicketsDefaults.CheckInRoleSystemName) != null)
            return;
        await _customerService.InsertCustomerRoleAsync(new CustomerRole
        {
            Name = EventTicketsDefaults.CheckInRoleName,
            SystemName = EventTicketsDefaults.CheckInRoleSystemName,
            Active = true,
            IsSystemRole = false,
            FreeShipping = false,
            TaxExempt = false
        });
    }

    public override async Task InstallAsync()
    {
        // Adresa a klíč zálohy do Google Tabulky přežijí odinstalaci (viz UninstallAsync), aby po nové
        // instalaci šly vstupenky hned obnovit z tabulky. Nová instalace je proto nepřepisuje.
        var previous = await _settingService.LoadSettingAsync<EventTicketsSettings>();
        await _settingService.SaveSettingAsync(new EventTicketsSettings
        {
            ProductIds = string.Empty,
            EventName = string.Empty,
            EventDate = string.Empty,
            EventPlace = string.Empty,
            EventNote = string.Empty,
            SendEmail = true,
            GoogleSheetUrl = previous.GoogleSheetUrl ?? string.Empty,
            GoogleSheetSecret = string.IsNullOrEmpty(previous.GoogleSheetSecret) ? TicketBackupService.NewSecret() : previous.GoogleSheetSecret
        });
        await EnsureCheckInRoleAsync();
        await EnsureBackupAsync();
        await base.InstallAsync();
    }

    public override async Task UpdateAsync(string currentVersion, string targetVersion)
    {
        await EnsureCheckInRoleAsync();
        await EnsureBackupAsync();
        // Z verze s jednou akcí: akce z nastavení se stane první akcí, vstupenky dostanou její ID.
        await _ticketService.EnsureEventsMigratedAsync();
        await base.UpdateAsync(currentVersion, targetVersion);
    }

    public override async Task UninstallAsync()
    {
        var task = await _scheduleTaskService.GetTaskByTypeAsync(EventTicketsDefaults.BackupTaskType);
        if (task != null)
            await _scheduleTaskService.DeleteTaskAsync(task);
        // Nastavení se smaže, kromě adresy a tajného klíče zálohy do Google Tabulky: bez nich by po nové
        // instalaci nešly vstupenky obnovit z tabulky.
        var settings = await _settingService.LoadSettingAsync<EventTicketsSettings>();
        await _settingService.DeleteSettingAsync<EventTicketsSettings>();
        if (!string.IsNullOrEmpty(settings.GoogleSheetUrl))
            await _settingService.SetSettingAsync("eventticketssettings.googlesheeturl", settings.GoogleSheetUrl);
        if (!string.IsNullOrEmpty(settings.GoogleSheetSecret))
            await _settingService.SetSettingAsync("eventticketssettings.googlesheetsecret", settings.GoogleSheetSecret);
        var role = await _customerService.GetCustomerRoleBySystemNameAsync(EventTicketsDefaults.CheckInRoleSystemName);
        if (role != null)
            await _customerService.DeleteCustomerRoleAsync(role);
        await base.UninstallAsync();
    }
}
