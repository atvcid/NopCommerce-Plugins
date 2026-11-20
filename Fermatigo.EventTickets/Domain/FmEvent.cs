using Nop.Core;

namespace Fermatigo.EventTickets.Domain;

/// <summary>Akce, na kterou se prodávají vstupenky. Vstupenkou akce je každý produkt z ProductIds.</summary>
public class FmEvent : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Datum a čas, volný text (např. 14. 3. 2027 od 18:00).</summary>
    public string EventDate { get; set; } = string.Empty;

    public string EventPlace { get; set; } = string.Empty;

    /// <summary>Doplňující text na vstupenku (dress code, parkování).</summary>
    public string EventNote { get; set; } = string.Empty;

    /// <summary>ID produktů vstupenek oddělená čárkou. Jeden produkt patří jen jedné akci.</summary>
    public string ProductIds { get; set; } = string.Empty;

    /// <summary>ID produktů VIP vstupenek (podmnožina ProductIds). Obsluha je po načtení QR kódu pozná podle zlatého pruhu.</summary>
    public string VipProductIds { get; set; } = string.Empty;

    public DateTime CreatedOnUtc { get; set; }
}
