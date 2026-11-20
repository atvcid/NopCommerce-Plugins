using Fermatigo.EventTickets.Domain;
using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;

namespace Fermatigo.EventTickets.Data;

/// <summary>
/// Tabulky vstupenek a akcí. Záměrně bez zpětné migrace: odinstalace pluginu tabulky NESMAŽE,
/// aby se vstupenky neztratily ani při odinstalaci a nové instalaci. Opětovná instalace
/// existující tabulky jen převezme.
/// </summary>
[NopMigration("2026/11/12 13:00:00", "Fermatigo.EventTickets: tabulka vstupenek", MigrationProcessType.Installation)]
public class SchemaMigration : Migration
{
    public override void Up()
    {
        // Create/Alter se provedou až po Up(), kontroly Schema hned – sloupce se proto doplňují jen u tabulek, které už existovaly.
        if (!Schema.Table(nameof(FmEventTicket)).Exists())
            Create.TableFor<FmEventTicket>();
        else if (!Schema.Table(nameof(FmEventTicket)).Column(nameof(FmEventTicket.EventId)).Exists())
            Alter.Table(nameof(FmEventTicket)).AddColumn(nameof(FmEventTicket.EventId)).AsInt32().NotNullable().WithDefaultValue(0);

        if (!Schema.Table(nameof(FmEvent)).Exists())
            Create.TableFor<FmEvent>();
        else if (!Schema.Table(nameof(FmEvent)).Column(nameof(FmEvent.VipProductIds)).Exists())
            Alter.Table(nameof(FmEvent)).AddColumn(nameof(FmEvent.VipProductIds)).AsString(1000).NotNullable().WithDefaultValue(string.Empty);
    }

    /// <summary>Nic nemaže: vstupenky a akce v databázi zůstávají i po odinstalaci.</summary>
    public override void Down() { }
}

/// <summary>Verze 1.5: více akcí. Tabulka akcí a sloupec EventId u vstupenek pro už nainstalovaný plugin.</summary>
[NopMigration("2026/11/16 12:00:00", "Fermatigo.EventTickets: více akcí", MigrationProcessType.Update)]
public class MultiEventMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(FmEvent)).Exists())
            Create.TableFor<FmEvent>();
        if (!Schema.Table(nameof(FmEventTicket)).Column(nameof(FmEventTicket.EventId)).Exists())
            Alter.Table(nameof(FmEventTicket)).AddColumn(nameof(FmEventTicket.EventId)).AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down() { }
}

/// <summary>Verze 1.9: VIP vstupenky, sloupec VipProductIds u akcí.</summary>
[NopMigration("2026/11/18 12:00:00", "Fermatigo.EventTickets: VIP vstupenky", MigrationProcessType.Update)]
public class VipMigration : Migration
{
    public override void Up()
    {
        if (!Schema.Table(nameof(FmEvent)).Column(nameof(FmEvent.VipProductIds)).Exists())
            Alter.Table(nameof(FmEvent)).AddColumn(nameof(FmEvent.VipProductIds)).AsString(1000).NotNullable().WithDefaultValue(string.Empty);
    }

    /// <summary>Nic nemaže.</summary>
    public override void Down() { }
}
