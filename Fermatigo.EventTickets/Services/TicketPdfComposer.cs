using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Fermatigo.EventTickets.Services;

public class TicketPdfData
{
    public string StoreName { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public IList<TicketPdfItem> Tickets { get; set; } = new List<TicketPdfItem>();
}

public class TicketPdfItem
{
    public string EventName { get; set; } = string.Empty;
    public bool IsVip { get; set; }
    public string When { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int Number { get; set; }
    public int Count { get; set; }
    public string HolderName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string CodeFormatted { get; set; } = string.Empty;
    public byte[] QrPng { get; set; } = Array.Empty<byte>();
}

/// <summary>PDF se vstupenkami, jedna vstupenka na stránku A4. Nezávisí na nopCommerce.</summary>
public static class TicketPdfComposer
{
    private const string Black = "#000000";
    private const string Font = "FreeSerif";
    private static readonly object FontLock = new();
    private static bool _fontRegistered;

    /// <summary>Písmo FreeSerif z nopCommerce (App_Data/Pdf) umí češtinu, registruje se jednou.</summary>
    public static void EnsureFont(string? fontPath)
    {
        if (_fontRegistered)
            return;
        lock (FontLock)
        {
            if (_fontRegistered)
                return;
            if (fontPath != null && File.Exists(fontPath))
            {
                using var stream = File.OpenRead(fontPath);
                FontManager.RegisterFont(stream);
            }
            _fontRegistered = true;
        }
    }

    public static byte[] Compose(TicketPdfData data)
    {
        var document = Document.Create(container =>
        {
            foreach (var ticket in data.Tickets)
            {
                container.Page(page =>
                {
                    // Tisková verze: jen černá na bílé, bez ploch barvy, šetří toner a dobře se tiskne i černobíle.
                    page.Size(PageSizes.A4);
                    page.Margin(48);
                    page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(12).FontColor(Black));

                    page.Content().Column(col =>
                    {
                        col.Spacing(8);

                        if (data.StoreName.Length > 0)
                            col.Item().Text(t => t.Span(data.StoreName.ToUpperInvariant()).FontSize(10).LetterSpacing(0.08f));
                        col.Item().Text(t => t.Span(ticket.EventName).FontSize(26).Bold());
                        if (ticket.When.Length > 0)
                            col.Item().Text(t => t.Span(ticket.When).FontSize(13));

                        col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Black);

                        if (ticket.IsVip)
                            col.Item().AlignCenter().Border(2).BorderColor(Black).PaddingVertical(6).PaddingHorizontal(22)
                                .Text(t => t.Span("VIP VSTUPENKA").FontSize(20).Bold().LetterSpacing(0.1f));
                        else
                            col.Item().AlignCenter().Text(t => t.Span("VSTUPENKA").FontSize(16).Bold().LetterSpacing(0.1f));

                        col.Item().PaddingTop(6).AlignCenter().Width(240).Height(240).Image(ticket.QrPng, ImageScaling.FitArea);

                        col.Item().AlignCenter().Text(t => t.Span(ticket.CodeFormatted).FontSize(18).Bold().LetterSpacing(0.15f));

                        col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Black);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(110);
                                c.RelativeColumn();
                            });
                            void Row(string label, string value)
                            {
                                table.Cell().PaddingBottom(5).Text(t => t.Span(label));
                                table.Cell().PaddingBottom(5).Text(t => t.Span(value).Bold());
                            }
                            Row("Jméno", ticket.HolderName);
                            if (ticket.ProductName.Length > 0)
                                Row("Vstupenka", ticket.ProductName);
                            Row("Objednávka", data.OrderNumber);
                        });

                        if (ticket.Note.Length > 0)
                            col.Item().PaddingTop(4).Text(t => t.Span(ticket.Note));

                        col.Item().PaddingTop(10).Text(t =>
                            t.Span("Vstupenku ukažte u vstupu vytištěnou nebo v mobilu. Každý QR kód platí pro jeden vstup, " +
                                   "po načtení u vstupu ho už nelze použít znovu. Vstupenku proto nikomu neposílejte ani nezveřejňujte.")
                                .FontSize(10));
                    });
                });
            }
        });

        return document.GeneratePdf();
    }
}
