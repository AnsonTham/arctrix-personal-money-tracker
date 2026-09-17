namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// A run of text found by OCR, located by the midpoints of its left and right edges in image
/// pixels (y grows downward).
/// </summary>
public readonly record struct OcrFragment(string Text, double LeftX, double LeftY, double RightX, double RightY, double Height)
{
    public static OcrFragment FromCorners(
        string text,
        double topLeftX, double topLeftY,
        double topRightX, double topRightY,
        double bottomRightX, double bottomRightY,
        double bottomLeftX, double bottomLeftY) =>
        new(
            text,
            (topLeftX + bottomLeftX) / 2, (topLeftY + bottomLeftY) / 2,
            (topRightX + bottomRightX) / 2, (topRightY + bottomRightY) / 2,
            Math.Sqrt(Math.Pow(bottomLeftX - topLeftX, 2) + Math.Pow(bottomLeftY - topLeftY, 2)));
}

/// <summary>
/// Rebuilds a receipt's visual rows from OCR fragments. Recognizers report a label and its price
/// ("Nasi Lemak ... 12.90") as separate pieces, and a photo taken at a slight angle puts the price
/// noticeably higher or lower than its label, so rows are grouped after undoing the page's tilt.
/// </summary>
public static class OcrLayout
{
    // Fragments whose centers are within this fraction of a line height belong to the same row.
    private const double RowTolerance = 0.6;

    /// <summary>One line of text per visual row, top to bottom, with a row's pieces left to right.</summary>
    public static string JoinIntoRows(IEnumerable<OcrFragment> fragments)
    {
        var pieces = fragments.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
        if (pieces.Count == 0)
            return string.Empty;

        var skew = EstimateSkew(pieces);
        var (sin, cos) = (Math.Sin(-skew), Math.Cos(-skew));
        var placed = pieces
            .Select(f =>
            {
                var centerX = (f.LeftX + f.RightX) / 2;
                var centerY = (f.LeftY + f.RightY) / 2;
                return new Placed(f.Text.Trim(), centerX * cos - centerY * sin, centerX * sin + centerY * cos, Math.Max(f.Height, 1));
            })
            .OrderBy(p => p.Y)
            .ToList();

        var rows = new List<List<Placed>>();
        foreach (var piece in placed)
        {
            var row = rows.LastOrDefault();
            if (row is not null
                && Math.Abs(piece.Y - row.Average(p => p.Y)) <= RowTolerance * Math.Max(piece.Height, row.Max(p => p.Height)))
            {
                row.Add(piece);
            }
            else
            {
                rows.Add([piece]);
            }
        }

        return string.Join('\n', rows.Select(row => string.Join("  ", row.OrderBy(p => p.X).Select(p => p.Text))));
    }

    // A tilted photo slants every line the same way; the median slope of the wider lines measures it.
    private static double EstimateSkew(List<OcrFragment> fragments)
    {
        var angles = fragments
            .Where(f => Math.Abs(f.RightX - f.LeftX) > 2 * f.Height)
            .Select(f => Math.Atan2(f.RightY - f.LeftY, f.RightX - f.LeftX))
            .OrderBy(a => a)
            .ToList();
        return angles.Count == 0 ? 0 : angles[angles.Count / 2];
    }

    private sealed record Placed(string Text, double X, double Y, double Height);
}
