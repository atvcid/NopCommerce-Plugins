using Fermatigo.EventTickets.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Extensions;
using Nop.Data.Mapping.Builders;

namespace Fermatigo.EventTickets.Data;

public class FmEventTicketBuilder : NopEntityBuilder<FmEventTicket>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(FmEventTicket.Code)).AsString(32).NotNullable().Unique()
            .WithColumn(nameof(FmEventTicket.HolderName)).AsString(400).NotNullable()
            .WithColumn(nameof(FmEventTicket.HolderEmail)).AsString(400).NotNullable()
            .WithColumn(nameof(FmEventTicket.CheckedInBy)).AsString(400).NotNullable()
            .WithColumn(nameof(FmEventTicket.OrderId)).AsInt32().NotNullable().Indexed()
            .WithColumn(nameof(FmEventTicket.EventId)).AsInt32().NotNullable().WithDefaultValue(0).Indexed();
    }
}

public class FmEventBuilder : NopEntityBuilder<FmEvent>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(FmEvent.Name)).AsString(400).NotNullable()
            .WithColumn(nameof(FmEvent.EventDate)).AsString(400).NotNullable()
            .WithColumn(nameof(FmEvent.EventPlace)).AsString(400).NotNullable()
            .WithColumn(nameof(FmEvent.ProductIds)).AsString(1000).NotNullable()
            .WithColumn(nameof(FmEvent.VipProductIds)).AsString(1000).NotNullable().WithDefaultValue(string.Empty);
    }
}
