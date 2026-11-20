using Nop.Web.Framework.Models;

namespace Fermatigo.EventTickets.Models;

/// <summary>
/// Textová pole jsou string?: u nenulového stringu by ASP.NET pole označil jako povinná
/// a kontrola v prohlížeči by tiše zastavila odeslání formuláře s prázdnou poznámkou.
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    public bool SendEmail { get; set; }

    public IList<EventRowModel> Events { get; set; } = new List<EventRowModel>();

    /// <summary>Upravovaná akce (Id 0 = nová), null = formulář akce zavřený.</summary>
    public EventEditModel? EditEvent { get; set; }

    /// <summary>Filtr seznamu vstupenek podle akce, 0 = všechny.</summary>
    public int EventId { get; set; }

    public string? GoogleSheetUrl { get; set; } = string.Empty;
    public string AppsScript { get; set; } = string.Empty;
    public string GoogleSheetSecret { get; set; } = string.Empty;

    /// <summary>Výsledek porovnání s archivem v Google Tabulce, null = obnova se neotevřela.</summary>
    public RestoreModel? Restore { get; set; }
    public string? BackupLastSuccess { get; set; }
    public string BackupLastError { get; set; } = string.Empty;
    public bool BackupTaskEnabled { get; set; }
    public int BackupIntervalMinutes { get; set; } = EventTicketsDefaults.DefaultBackupIntervalMinutes;

    public int CheckInRoleId { get; set; }
    public int Total { get; set; }
    public int CheckedIn { get; set; }
    public int Canceled { get; set; }

    public string? Search { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageCount { get; set; }
    public int Found { get; set; }
    public IList<TicketRowModel> Tickets { get; set; } = new List<TicketRowModel>();
}

public record RestoreModel
{
    public string Error { get; set; } = string.Empty;
    public int InArchive { get; set; }
    public int InShop { get; set; }
    public IList<RestoreRowModel> Rows { get; set; } = new List<RestoreRowModel>();
}

public record RestoreRowModel
{
    public string Code { get; set; } = string.Empty;
    public string CodeFormatted { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public bool OrderExists { get; set; }
    public int Number { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string HolderEmail { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string LastSeen { get; set; } = string.Empty;

    /// <summary>Podle poznámky objednávky ji někdo smazal ručně, nabízí se nezaškrtnutá.</summary>
    public bool ManuallyDeleted { get; set; }
}

public record EventRowModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string When { get; set; } = string.Empty;
    public IList<string> Products { get; set; } = new List<string>();
    public int Total { get; set; }
    public int CheckedIn { get; set; }
    public int Canceled { get; set; }
    /// <summary>Všechny vstupenky akce včetně zneplatněných, smažou se spolu s akcí.</summary>
    public int TicketCount { get; set; }
}

public record EventEditModel
{
    public int Id { get; set; }
    public string? Name { get; set; } = string.Empty;
    public string? EventDate { get; set; } = string.Empty;
    public string? EventPlace { get; set; } = string.Empty;
    public string? EventNote { get; set; } = string.Empty;
    public string? ProductIds { get; set; } = string.Empty;
    public string? VipProductIds { get; set; } = string.Empty;
    public IList<string> ProductNames { get; set; } = new List<string>();
}

public record TicketRowModel
{
    public bool IsVip { get; set; }
    public string EventName { get; set; } = string.Empty;
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int Number { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string HolderEmail { get; set; } = string.Empty;
    public string? CheckedIn { get; set; }
    public string CheckedInBy { get; set; } = string.Empty;
    public bool Canceled { get; set; }

    /// <summary>Objednávka je stornovaná, vstupenka se ukazuje jako „storno“.</summary>
    public bool OrderCanceled { get; set; }
    public string Url { get; set; } = string.Empty;
}
