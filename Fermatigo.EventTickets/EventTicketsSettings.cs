using Nop.Core.Configuration;

namespace Fermatigo.EventTickets;

public class EventTicketsSettings : ISettings
{
    /// <summary>ID produktů, které jsou vstupenkou, oddělená čárkou.</summary>
    public string ProductIds { get; set; } = string.Empty;

    /// <summary>Název akce na vstupence a v e-mailu.</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Datum a čas akce, volný text (např. 14. 3. 2027 od 18:00).</summary>
    public string EventDate { get; set; } = string.Empty;

    /// <summary>Místo konání, volný text.</summary>
    public string EventPlace { get; set; } = string.Empty;

    /// <summary>Doplňující text na vstupenku (např. dress code, parkování).</summary>
    public string EventNote { get; set; } = string.Empty;

    /// <summary>Po zaplacení poslat zákazníkovi e-mail s PDF vstupenkami.</summary>
    public bool SendEmail { get; set; } = true;

    /// <summary>Adresa webové aplikace Google Apps Script, která zapisuje zálohu do Google Tabulky.</summary>
    public string GoogleSheetUrl { get; set; } = string.Empty;

    /// <summary>Tajný klíč, který musí znát skript v Google Tabulce. Vzniká sám při instalaci.</summary>
    public string GoogleSheetSecret { get; set; } = string.Empty;

    /// <summary>Otisk naposledy úspěšně odeslaných dat, aby se neposílalo pořád to samé.</summary>
    public string BackupLastHash { get; set; } = string.Empty;

    /// <summary>Čas poslední úspěšné zálohy v UTC (ISO 8601), prázdné = zatím žádná.</summary>
    public string BackupLastSuccessUtc { get; set; } = string.Empty;

    /// <summary>Text poslední chyby zálohy, prázdné = poslední pokus prošel.</summary>
    public string BackupLastError { get; set; } = string.Empty;

    /// <summary>Interval zálohy v minutách (výchozí 1440 = jednou denně). Zapisuje se i do sekund plánované úlohy.</summary>
    public int BackupIntervalMinutes { get; set; } = EventTicketsDefaults.DefaultBackupIntervalMinutes;

    /// <summary>Čas posledního odeslání zálohy v UTC (ISO 8601), úspěšného i neúspěšného.</summary>
    public string BackupLastAttemptUtc { get; set; } = string.Empty;
}
