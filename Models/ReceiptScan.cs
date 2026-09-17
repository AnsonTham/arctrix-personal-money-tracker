namespace Arctrix.PersonalMoneyTracker.Models;

/// <summary>A purchased item read from a receipt: best effort, and only shown for reference.</summary>
public sealed record ReceiptLineItem(string Description, decimal Amount);

/// <summary>What <see cref="Services.ReceiptParser"/> could read from a receipt. Every field may be missing or wrong.</summary>
public sealed record ReceiptScanResult(
    string? ShopName,
    decimal? Total,
    DateTime? Date,
    IReadOnlyList<ReceiptLineItem> Items,
    string? SuggestedCategory)
{
    /// <summary>
    /// Worth pre-filling a form with: a total or line items were found, or a shop name backed by a
    /// date. A name on its own is too easily picked out of noise.
    /// </summary>
    public bool HasUsefulData => Total is not null || Items.Count > 0 || (ShopName is not null && Date is not null);
}

/// <summary>
/// Hands a scanned receipt to the transaction form: the stored photo, and what was read from it
/// (null when nothing usable was found, so the form opens empty).
/// </summary>
public sealed record ReceiptDraft(string PhotoPath, ReceiptScanResult? Scan);
