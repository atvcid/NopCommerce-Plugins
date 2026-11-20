using Nop.Core;

namespace Fermatigo.EventTickets.Domain;

/// <summary>Jedna vstupenka = jeden kus produktu v zaplacené objednávce = jeden QR kód.</summary>
public class FmEventTicket : BaseEntity
{
    /// <summary>Náhodný kód vstupenky (5 znaků, starší vstupenky 20), obsah QR kódu.</summary>
    public string Code { get; set; } = string.Empty;

    public int OrderId { get; set; }
    public int OrderItemId { get; set; }
    public int ProductId { get; set; }

    /// <summary>Akce vstupenky (FmEvent). 0 jen u vstupenek z verzí před 1.5, plugin je při aktualizaci doplní.</summary>
    public int EventId { get; set; }

    /// <summary>Pořadí vstupenky v objednávce (1, 2, 3, ...).</summary>
    public int Number { get; set; }

    public string HolderName { get; set; } = string.Empty;
    public string HolderEmail { get; set; } = string.Empty;

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? CheckedInOnUtc { get; set; }
    public string CheckedInBy { get; set; } = string.Empty;

    /// <summary>Zneplatněná vstupenka (vrácení peněz, storno objednávky nebo ručně).</summary>
    public bool Canceled { get; set; }
}
