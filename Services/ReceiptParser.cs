using System.Globalization;
using System.Text.RegularExpressions;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Best-effort reading of recognized receipt text: shop name, total, date, line items and a
/// suggested category. Pure logic with no platform dependencies. Receipts vary a lot, so every
/// value is a suggestion the user reviews before anything is saved.
/// </summary>
public static partial class ReceiptParser
{
    private const int HeaderRows = 8;
    private const int MaxItems = 12;
    private const int MaxShopNameLength = 60;
    private const int MaxDescriptionLength = 60;

    /// <param name="text">Recognized text, one visual row per line.</param>
    /// <param name="today">Today's date; receipt dates after tomorrow are rejected as misreads.</param>
    public static ReceiptScanResult Parse(string? text, DateTime today)
    {
        var rows = (text ?? string.Empty)
            .Split('\n')
            .Select(row => WhitespaceRun().Replace(row, " ").Trim())
            .Where(row => row.Length > 0)
            .ToList();

        var shopName = FindShopName(rows, today);
        return new ReceiptScanResult(
            shopName,
            FindTotal(rows),
            FindDate(rows, today),
            FindItems(rows, today),
            SuggestCategory(shopName, rows));
    }

    // ---------------------------------------------------------------- Total

    /// <summary>The amount on the last total-like row; receipts list a subtotal before the total.</summary>
    private static decimal? FindTotal(List<string> rows)
    {
        decimal? total = null;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!TotalKeyword().IsMatch(rows[i]) || NotTheTotal().IsMatch(rows[i]))
                continue;

            // The amount is usually on the same row; some layouts put it alone on the next one.
            var amount = LastAmount(rows[i]);
            if (amount is null && i + 1 < rows.Count && AmountOnlyRow().IsMatch(rows[i + 1]))
                amount = LastAmount(rows[i + 1]);

            if (amount is not null)
                total = amount;
        }
        return total;
    }

    private static decimal? LastAmount(string row)
    {
        var matches = MoneyAmount().Matches(row);
        return matches.Count == 0 ? null : ParseAmount(matches[^1].Groups["amount"].Value);
    }

    private static decimal? ParseAmount(string value)
    {
        // "28,20" is a decimal comma; "1,234.50" uses the comma for thousands.
        var normalized = DecimalCommaAmount().IsMatch(value) ? value.Replace(',', '.') : value.Replace(",", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0
            ? amount
            : null;
    }

    // ---------------------------------------------------------------- Date

    private static DateTime? FindDate(List<string> rows, DateTime today)
    {
        foreach (var row in rows)
        {
            if (FindDateIn(row, today) is DateTime date)
                return date;
        }
        return null;
    }

    private static DateTime? FindDateIn(string row, DateTime today)
    {
        if (YearFirstDate().Match(row) is { Success: true } ymd
            && ToDate(ymd.Groups["y"].Value, ymd.Groups["m"].Value, ymd.Groups["d"].Value, today) is DateTime isoDate)
            return isoDate;

        // Numeric dates are read day-first (the local convention), then month-first if that's impossible.
        if (NumericDate().Match(row) is { Success: true } numeric)
        {
            var (a, b, y) = (numeric.Groups["a"].Value, numeric.Groups["b"].Value, numeric.Groups["y"].Value);
            if ((ToDate(y, b, a, today) ?? ToDate(y, a, b, today)) is DateTime numericDate)
                return numericDate;
        }

        if (DayMonthNameDate().Match(row) is { Success: true } dmy
            && ToDate(dmy.Groups["y"].Value, MonthNumber(dmy.Groups["mon"].Value), dmy.Groups["d"].Value, today) is DateTime dayFirst)
            return dayFirst;

        if (MonthNameDayDate().Match(row) is { Success: true } mdy
            && ToDate(mdy.Groups["y"].Value, MonthNumber(mdy.Groups["mon"].Value), mdy.Groups["d"].Value, today) is DateTime monthFirst)
            return monthFirst;

        return null;
    }

    private static DateTime? ToDate(string year, string month, string day, DateTime today)
    {
        if (!int.TryParse(year, out var y) || !int.TryParse(month, out var m) || !int.TryParse(day, out var d))
            return null;
        if (y < 100)
            y += 2000;
        if (m is < 1 or > 12 || d < 1 || y < 1 || d > DateTime.DaysInMonth(Math.Min(y, 9999), m))
            return null;

        var date = new DateTime(y, m, d);
        return date <= today.Date.AddDays(1) && date >= today.Date.AddYears(-10) ? date : null;
    }

    private static string MonthNumber(string name) =>
        (Array.IndexOf(MonthPrefixes, name[..3].ToLowerInvariant()) + 1).ToString(CultureInfo.InvariantCulture);

    private static readonly string[] MonthPrefixes = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    // ---------------------------------------------------------------- Shop name

    /// <summary>The first header row that reads like a name rather than an address, phone number or label.</summary>
    private static string? FindShopName(List<string> rows, DateTime today)
    {
        var header = rows.Take(HeaderRows).ToList();
        for (var i = 0; i < header.Count; i++)
        {
            if (CleanNameRow(header[i], today) is not string name)
                continue;

            // A very short first row ("AB") is often the first half of a split name.
            if (LetterCount(name) <= 3 && i + 1 < header.Count && CleanNameRow(header[i + 1], today) is string next)
                name = $"{name} {next}";

            if (name.Length > MaxShopNameLength)
                name = name[..MaxShopNameLength].TrimEnd();
            return IsAllCaps(name) ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()) : name;
        }
        return null;
    }

    private static string? CleanNameRow(string row, DateTime today)
    {
        var cleaned = RegistrationNumber().Replace(WelcomePrefix().Replace(row, string.Empty), string.Empty)
            .Trim(' ', '-', '*', '=', '.', ':', '#');

        var looksLikeName = LetterCount(cleaned) >= 3
            && LetterCount(cleaned) >= cleaned.Count(char.IsDigit)
            && !PhoneLine().IsMatch(cleaned)
            && !AddressLine().IsMatch(cleaned)
            && !HeaderNoise().IsMatch(cleaned)
            && !MoneyAmount().IsMatch(cleaned)
            && FindDateIn(cleaned, today) is null;
        return looksLikeName ? cleaned : null;
    }

    private static int LetterCount(string text) => text.Count(char.IsLetter);

    private static bool IsAllCaps(string text) => text.Any(char.IsLetter) && text == text.ToUpperInvariant();

    // ---------------------------------------------------------------- Line items

    /// <summary>Rows shaped like "description ... amount" that aren't totals, taxes or payments.</summary>
    private static IReadOnlyList<ReceiptLineItem> FindItems(List<string> rows, DateTime today)
    {
        var items = new List<ReceiptLineItem>();
        foreach (var row in rows)
        {
            // An amount on its own ("RM 29.04") belongs to the total above it, not a purchase.
            if (ItemRow().Match(row) is not { Success: true } match
                || AmountOnlyRow().IsMatch(row)
                || NotAnItem().IsMatch(row)
                || PhoneLine().IsMatch(row)
                || FindDateIn(row, today) is not null
                || ParseAmount(match.Groups["amount"].Value) is not decimal amount)
                continue;

            var description = match.Groups["desc"].Value.Trim(' ', '-', '.', ':', '*');
            if (LetterCount(description) < 2)
                continue;
            if (description.Length > MaxDescriptionLength)
                description = description[..MaxDescriptionLength].TrimEnd();

            items.Add(new ReceiptLineItem(description, amount));
            if (items.Count == MaxItems)
                break;
        }
        return items;
    }

    // ---------------------------------------------------------------- Category

    /// <summary>
    /// A starting suggestion from keywords in the shop name, falling back to the receipt's header rows.
    /// Returns a default category name (Food, Drinks, Transport, ...) or null.
    /// </summary>
    private static string? SuggestCategory(string? shopName, List<string> rows)
    {
        var header = string.Join(' ', rows.Take(5));
        foreach (var source in new[] { shopName, header })
        {
            if (string.IsNullOrWhiteSpace(source))
                continue;
            foreach (var (category, keywords) in CategoryKeywords)
            {
                if (keywords.IsMatch(source))
                    return category;
            }
        }
        return null;
    }

    // Checked in order: a coffee chain is Drinks before "cafe" makes it Food.
    private static readonly (string Category, Regex Keywords)[] CategoryKeywords =
    [
        ("Transport", TransportKeywords()),
        ("Bills", BillsKeywords()),
        ("Entertainment", EntertainmentKeywords()),
        ("Drinks", DrinksKeywords()),
        ("Food", FoodKeywords()),
        ("Shopping", ShoppingKeywords()),
    ];

    // ---------------------------------------------------------------- Patterns

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    // A money amount with two decimals: 1,234.56 / 28.20 / 28,20.
    [GeneratedRegex(@"(?<![\d.,])(?<amount>\d{1,3}(?:,\d{3})+\.\d{2}|\d+[.,]\d{2})(?!\d|[.,]\d)")]
    private static partial Regex MoneyAmount();

    [GeneratedRegex(@"^\d+,\d{2}$")]
    private static partial Regex DecimalCommaAmount();

    [GeneratedRegex(@"^(?:(?:RM|MYR|USD|SGD|\$)\s*)?\d[\d,]*[.,]\d{2}$", RegexOptions.IgnoreCase)]
    private static partial Regex AmountOnlyRow();

    [GeneratedRegex(@"\b(grand\s*total|total|amount\s*(due|payable)|balance\s*due|jumlah)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TotalKeyword();

    [GeneratedRegex(@"\b(sub\s*-?\s*total|total\s*(qty|quantity|items?|disc(ount)?|savings?|tax|gst|sst|excl\w*)|(tax|gst|sst|service)\s*total|rounding)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheTotal();

    [GeneratedRegex(@"\b(?<y>(19|20)\d{2})[-/.](?<m>\d{1,2})[-/.](?<d>\d{1,2})\b")]
    private static partial Regex YearFirstDate();

    [GeneratedRegex(@"\b(?<a>\d{1,2})[-/.](?<b>\d{1,2})[-/.](?<y>\d{4}|\d{2})\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\b(?<d>\d{1,2})[\s\-/.]*(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?[\s\-/.,]*(?<y>\d{4}|\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DayMonthNameDate();

    [GeneratedRegex(@"\b(?<mon>jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(?<d>\d{1,2}),?\s+(?<y>\d{4})\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthNameDayDate();

    [GeneratedRegex(@"\b(tel|phone|fax|hp|h/p|mobile|whatsapp)\b|\+?\d[\d\s-]{7,}\d", RegexOptions.IgnoreCase)]
    private static partial Regex PhoneLine();

    [GeneratedRegex(@"\b(jalan|jln|lorong|lrg|taman|tmn|persiaran|lebuh|lebuhraya|street|road|avenue|lot|unit|floor|level|blok|block|kampung|kg|bandar|seksyen|section)\b|\bno\.?\s*\d|\b\d{5}\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddressLine();

    [GeneratedRegex(@"\b(tax\s*invoice|invoice|receipt|resit|cash\s*(sale|bill)|official|gst|sst|reg(istration)?\.?\s*no|co\.?\s*(no|reg)|company\s*no|thank\s*you|table|cashier|order|date|time)\b|www\.|https?:|@|\.com\b", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderNoise();

    [GeneratedRegex(@"^\s*welcome\s+to\s+", RegexOptions.IgnoreCase)]
    private static partial Regex WelcomePrefix();

    // Company registration numbers printed after the name, e.g. "(1234567-X)".
    [GeneratedRegex(@"\(\s*[\dA-Z][\dA-Z\-]{4,}\s*\)")]
    private static partial Regex RegistrationNumber();

    [GeneratedRegex(@"^(?<desc>.*?[A-Za-z].*?)\s+(?:(?:RM|MYR|\$)\s*)?(?<amount>\d{1,3}(?:,\d{3})+\.\d{2}|\d+[.,]\d{2})\s*[A-Za-z*]?$")]
    private static partial Regex ItemRow();

    [GeneratedRegex(@"\b(total|sub\s*total|subtotal|tax|gst|sst|service|svc|rounding|round|change|cash|card|visa|master(card)?|amex|credit|debit|tender(ed)?|paid|payment|balance|discount|disc|voucher|tip|qty|points?|jumlah|tunai|baki|amount|due|deposit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotAnItem();

    [GeneratedRegex(@"\b(shell|petronas|petron|esso|caltex|bhp|fuel|petrol|parking|parkir|toll|tol|touch\s*'?n\s*go|rapid\s*kl|mrt|lrt|ktm|grab|taxi|car\s*wash|tyres?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TransportKeywords();

    [GeneratedRegex(@"\b(tnb|tenaga\s*nasional|air\s*selangor|syabas|indah\s*water|unifi|telekom|maxis|celcom|digi|u\s*mobile|astro|insurance|takaful|electricity|water\s*bill|internet)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BillsKeywords();

    [GeneratedRegex(@"\b(cinema|cinemas|gsc|golden\s*screen|tgv|mbo|netflix|spotify|karaoke|bowling|arcade|theme\s*park|concert|museum|zoo)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EntertainmentKeywords();

    [GeneratedRegex(@"\b(starbucks|coffee\s*bean|zus|tealive|chatime|boba|bubble\s*tea|coffee|juice|brew(ery)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DrinksKeywords();

    [GeneratedRegex(@"\b(restaurant|restoran|kedai\s*makan|cafe|café|kopitiam|bistro|eatery|kitchen|bakery|mamak|nasi|mcdonald'?s|kfc|pizza|burger|domino'?s|subway|sushi|ramen|dim\s*sum|marrybrown|texas\s*chicken|secret\s*recipe|nando'?s|grill|food|makan)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FoodKeywords();

    [GeneratedRegex(@"\b(mall|mart|store|supermarket|hypermarket|aeon|giant|tesco|lotus'?s|mydin|jaya\s*grocer|village\s*grocer|speedmart|7-?eleven|family\s*mart|watsons|guardian|uniqlo|zara|ikea|mr\.?\s*diy|daiso|shopee|lazada|pharmacy|farmasi|popular)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShoppingKeywords();
}
