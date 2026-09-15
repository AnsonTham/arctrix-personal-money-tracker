using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Arctrix.PersonalMoneyTracker.Models;
// MAUI globally imports Microsoft.Maui.Graphics, which also defines Colors.
using Colors = QuestPDF.Helpers.Colors;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>QuestPDF monthly report. Compiled for Windows only (see the .csproj).</summary>
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

    public bool IsSupported => true;

    public async Task<string> GenerateMonthlyReportAsync(int year, int month)
    {
        var settings = await _settings.GetAsync();
        var currentBase = settings.BaseCurrency;
        var monthTx = await _transactions.GetForMonthAsync(year, month);
        var categories = await _categories.GetAllAsync(includeArchived: true);
        var accounts = await _accounts.GetAllAsync();

        // Net worth is current state, converted live. Month totals stay in each transaction's
        // recorded base currency, so a past month isn't relabeled after a base currency change.
        var netWorth = accounts.Sum(a => _accounts.BalanceIn(a, currentBase));
        var flows = await _transactions.GetMonthlyFlowsAsync(new DateTime(year, month, 1), 1);
        var monthCurrency = MoneySummary.PickPrimary(flows.Select(f => f.Currency), currentBase);
        var income = new MoneySummary(flows.Select(f => new CurrencyAmount(f.Currency, f.Income)), monthCurrency);
        var expense = new MoneySummary(flows.Select(f => new CurrencyAmount(f.Currency, f.Expense)), monthCurrency);
        var byCategory = await _transactions.GetCategorySpendAsync(year, month);
        var recordedElsewhere = monthTx.Any(t => t.BaseCurrencyAtEntry != currentBase);
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");

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
                            c.Item().Text("Net Worth (today)").FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{currentBase} {netWorth:N2}").FontSize(16).Bold();
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Income").FontColor(Colors.Grey.Darken1);
                            c.Item().Text(income.Label).FontSize(16).Bold().FontColor(Colors.Green.Darken1);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Expense").FontColor(Colors.Grey.Darken1);
                            c.Item().Text(expense.Label).FontSize(16).Bold().FontColor(Colors.Red.Darken1);
                        });
                    });

                    if (recordedElsewhere)
                    {
                        col.Item()
                            .Text($"Month amounts are shown in the base currency that was active when each transaction was recorded (today's is {currentBase}).")
                            .FontSize(9)
                            .FontColor(Colors.Grey.Darken1);
                    }

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

                        foreach (var row in byCategory.OrderBy(c => c.Currency != monthCurrency).ThenByDescending(c => c.Amount))
                        {
                            table.Cell().Text(row.Name);
                            table.Cell().AlignRight().Text(row.AmountLabel);
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
                            var sign = t.Type == TransactionType.Income ? "+ " : t.Type == TransactionType.Expense ? "- " : "";
                            table.Cell().Text(t.Date.ToString("d MMM"));
                            table.Cell().Text(catName);
                            table.Cell().Text(string.IsNullOrWhiteSpace(t.Notes) ? "-" : t.Notes);
                            table.Cell().AlignRight().Text($"{sign}{t.BaseCurrencyAtEntry} {t.BaseAmount:N2}");
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
