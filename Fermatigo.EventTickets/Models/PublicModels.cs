using Fermatigo.EventTickets.Services;

namespace Fermatigo.EventTickets.Models;

public record EventInfoModel
{
    public string StoreName { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string EventDate { get; set; } = string.Empty;
    public string EventPlace { get; set; } = string.Empty;
    public string EventNote { get; set; } = string.Empty;
}

public record OrderTicketsModel
{
    public EventInfoModel Event { get; set; } = new();

    /// <summary>Objednávka má vstupenky na více akcí, akce se pak ukazuje u každé vstupenky.</summary>
    public bool MultiEvent { get; set; }
    public Guid OrderGuid { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public IList<PublicTicketModel> Tickets { get; set; } = new List<PublicTicketModel>();
}

public record PublicTicketModel
{
    public string EventName { get; set; } = string.Empty;
    public string EventWhen { get; set; } = string.Empty;
    public string EventNote { get; set; } = string.Empty;
    public bool IsVip { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CodeFormatted { get; set; } = string.Empty;
    public int Number { get; set; }
    public int Count { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public bool Canceled { get; set; }
    public bool CheckedIn { get; set; }
}

public record ScanModel
{
    public EventInfoModel Event { get; set; } = new();
    public string Code { get; set; } = string.Empty;
    public bool IsStaff { get; set; }
    public bool IsRegistered { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;

    /// <summary>Výsledek právě provedeného odbavení, jinak null (jen náhled).</summary>
    public CheckInStatus? Result { get; set; }

    public bool Found { get; set; }
    public bool Canceled { get; set; }
    public string? CheckedInAt { get; set; }
    public string CheckedInBy { get; set; } = string.Empty;
    public int Number { get; set; }
    public int Count { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string HolderEmail { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    /// <summary>VIP vstupenka: obsluha uvidí velký zlatý pruh VIP.</summary>
    public bool IsVip { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public bool PaymentOk { get; set; }
}
