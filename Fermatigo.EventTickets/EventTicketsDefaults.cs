namespace Fermatigo.EventTickets;

public static class EventTicketsDefaults
{
    public const string SystemName = "Fermatigo.EventTickets";

    /// <summary>Role zákazníka, která smí vstupenky odbavovat (kromě administrátorů).</summary>
    public const string CheckInRoleSystemName = "FmTicketCheckIn";
    public const string CheckInRoleName = "Odbavení vstupenek";

    /// <summary>Znaky kódu vstupenky: bez 0/O, 1/I/L, aby šel kód případně přečíst a přepsat ručně.</summary>
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Délka kódu nových vstupenek (31^5, asi 28,6 milionu kombinací).</summary>
    public const int CodeLength = 5;

    /// <summary>Délka kódu vstupenek z verzí 1.0 a 1.1, ty dál platí.</summary>
    public const int LegacyCodeLength = 20;

    public const string AdminRoutePrefix = "Admin/FmTickets/";

    /// <summary>Plánovaná úloha zálohy do Google Tabulky.</summary>
    public const string BackupTaskName = "Fermatigo vstupenky: záloha do Google Tabulky";
    public const string BackupTaskType = "Fermatigo.EventTickets.Services.TicketBackupTask, Fermatigo.EventTickets";
    /// <summary>Výchozí interval zálohy: jednou denně.</summary>
    public const int DefaultBackupIntervalMinutes = 1440;

    /// <summary>Sekundy plánované úlohy pro daný interval zálohy v minutách.</summary>
    public static int BackupTaskSeconds(int minutes) => Math.Max(1, minutes) * 60;

    /// <summary>Volby intervalu zálohy v administraci (minuty, popisek).</summary>
    public static readonly (int Minutes, string Label)[] BackupIntervals =
    {
        (1, "každou minutu"), (5, "každých 5 minut"), (15, "každých 15 minut"), (30, "každých 30 minut"),
        (60, "každou hodinu"), (180, "každé 3 hodiny"), (360, "každých 6 hodin"), (720, "každých 12 hodin"),
        (1440, "jednou denně")
    };

    public static string BackupIntervalLabel(int minutes)
    {
        foreach (var i in BackupIntervals)
            if (i.Minutes == minutes)
                return i.Label;
        return $"každých {minutes} minut";
    }
}
