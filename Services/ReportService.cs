using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Arctrix.PersonalMoneyTracker.Models;
// MAUI globally imports Microsoft.Maui.Graphics, which also defines Colors.
using Colors = QuestPDF.Helpers.Colors;

namespace Arctrix.PersonalMoneyTracker.Services;

public interface IReportService
{
    /// <summary>Builds a monthly PDF summary and returns the saved file path.</summary>
    Task<string> GenerateMonthlyReportAsync(int year, int month);
}

public class ReportService : IReportService
{
    private readonly ITransactionService _transactions;
    private readonly ICategoryService _categories;
    private readonly IAccountService _accounts;
    private readonly ISettingsService _settings;

    public ReportService(
        ITransactionService transactions,
        ICategoryService categories,
        IAccountService accounts,
        ISettingsService settings)
    {
        _transactions = transactions;
        _categories = categories;
        _accounts = accounts;
        _settings = settings;
    }

    public async Task<string> GenerateMonthlyReportAsync(int year, int month)
    {
        var settings = await _settings.GetAsync();
        var currency = settings.BaseCurrency;
        var monthTx = await _transactions.GetForMonthAsync(year, month);
        var categories = await _categories.GetAllAsync(includeArchived: true);
        var accounts = await _accounts.GetAllAsync(includeArchived: true);

        var income = monthTx.Where(t => t.Type == TransactionType.Income).Sum(t => t.BaseAmount);
        var expense = monthTx.Where(t => t.Type == TransactionType.Expense).Sum(t => t.BaseAmount);
        var netWorth = accounts.Sum(a => a.Balance);
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");

        var byCategory = monthTx
            .Where(t => t.Type == TransactionType.Expense)
            .GroupBy(t => t.CategoryId)
            .Select(g => new
            {
                Name = categories.FirstOrDefault(c => c.Id == g.Key)?.Name ?? "Others",
                Total = g.Sum(t => t.BaseAmount)
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Item().Text("Arctrix Personal Money Tracker").FontSize(18).Bold();
                    col.Item().Text($"Monthly Report — {monthName}").FontSize(12).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(16).Column(col =>
                {
                    col.Spacing(14);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Net Worth").FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{currency} {netWorth:N2}").FontSize(16).Bold();
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Income").FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{currency} {income:N2}").FontSize(16).Bold().FontColor(Colors.Green.Darken1);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Expense").FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{currency} {expense:N2}").FontSize(16).Bold().FontColor(Colors.Red.Darken1);
                        });
                    });

                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                    col.Item().Text("Spending by Category").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Category").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                        });

                        foreach (var row in byCategory)
                        {
                            table.Cell().Text(row.Name);
                            table.Cell().AlignRight().Text($"{currency} {row.Total:N2}");
                        }
                    });

                    col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                    col.Item().Text("Transactions").Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.2f);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
                            c.RelativeColumn(1.5f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("Date").Bold();
                            header.Cell().Text("Category").Bold();
                            header.Cell().Text("Notes").Bold();
                            header.Cell().AlignRight().Text("Amount").Bold();
                        });

                        foreach (var t in monthTx.OrderBy(t => t.Date))
                        {
                            var catName = categories.FirstOrDefault(c => c.Id == t.CategoryId)?.Name ?? "Others";
                            table.Cell().Text(t.Date.ToString("d MMM"));
                            table.Cell().Text(catName);
                            table.Cell().Text(string.IsNullOrWhiteSpace(t.Notes) ? "-" : t.Notes);
                            table.Cell().AlignRight().Text($"{(t.Type == TransactionType.Income ? "+" : "-")} {currency} {t.BaseAmount:N2}");
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated by Arctrix Solutions — ").FontColor(Colors.Grey.Darken1);
                    x.Span(DateTime.Now.ToString("d MMM yyyy, h:mm tt")).FontColor(Colors.Grey.Darken1);
                });
            });
        });

        var fileName = $"Arctrix-Report-{year:D4}-{month:D2}.pdf";
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);
        document.GeneratePdf(path);
        return path;
    }
}
