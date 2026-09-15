using System.Globalization;
using Arctrix.PersonalMoneyTracker.Models;
using GFont = Microsoft.Maui.Graphics.Font;
using HorizontalAlignment = Microsoft.Maui.Graphics.HorizontalAlignment;
using VerticalAlignment = Microsoft.Maui.Graphics.VerticalAlignment;

namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>
/// Paired columns of income and spending per month. Mark specs: columns at most 24px wide,
/// square at the baseline with a 4px rounded top, a 2px gap within each pair, a hairline
/// grid, and a per-month readout on hover or tap listing both values.
/// </summary>
public class FlowBarChart : GraphicsView, IDrawable
{
    private const float LeftBand = 44;
    private const float RightPadding = 8;
    private const float TopPadding = 10;
    private const float AxisBand = 24;
    private const float MaxBarWidth = 24;
    private const float PairGap = 2;

    public static readonly BindableProperty FlowsProperty = BindableProperty.Create(
        nameof(Flows), typeof(IReadOnlyList<MonthlyFlow>), typeof(FlowBarChart), null, propertyChanged: Redraw);

    public static readonly BindableProperty IncomeColorProperty = BindableProperty.Create(
        nameof(IncomeColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#22D3A2"), propertyChanged: Redraw);

    public static readonly BindableProperty ExpenseColorProperty = BindableProperty.Create(
        nameof(ExpenseColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#E5475F"), propertyChanged: Redraw);

    public static readonly BindableProperty GridColorProperty = BindableProperty.Create(
        nameof(GridColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#252C35"), propertyChanged: Redraw);

    public static readonly BindableProperty LabelColorProperty = BindableProperty.Create(
        nameof(LabelColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#8F98A7"), propertyChanged: Redraw);

    public static readonly BindableProperty ValueColorProperty = BindableProperty.Create(
        nameof(ValueColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#EEF1F5"), propertyChanged: Redraw);

    public static readonly BindableProperty TooltipColorProperty = BindableProperty.Create(
        nameof(TooltipColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#171C23"), propertyChanged: Redraw);

    public static readonly BindableProperty HoverColorProperty = BindableProperty.Create(
        nameof(HoverColor), typeof(Color), typeof(FlowBarChart), Color.FromArgb("#171C23"), propertyChanged: Redraw);

    public static readonly BindableProperty ValuePrefixProperty = BindableProperty.Create(
        nameof(ValuePrefix), typeof(string), typeof(FlowBarChart), string.Empty, propertyChanged: Redraw);

    private RectF _plot;
    private int? _hoverIndex;

    public FlowBarChart()
    {
        Drawable = this;

        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += (_, e) => SetHover(e.GetPosition(this));
        pointer.PointerExited += (_, _) => SetHover(null);
        GestureRecognizers.Add(pointer);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) => SetHover(e.GetPosition(this));
        GestureRecognizers.Add(tap);
    }

    public IReadOnlyList<MonthlyFlow>? Flows
    {
        get => (IReadOnlyList<MonthlyFlow>?)GetValue(FlowsProperty);
        set => SetValue(FlowsProperty, value);
    }

    public Color IncomeColor
    {
        get => (Color)GetValue(IncomeColorProperty);
        set => SetValue(IncomeColorProperty, value);
    }

    public Color ExpenseColor
    {
        get => (Color)GetValue(ExpenseColorProperty);
        set => SetValue(ExpenseColorProperty, value);
    }

    public Color GridColor
    {
        get => (Color)GetValue(GridColorProperty);
        set => SetValue(GridColorProperty, value);
    }

    public Color LabelColor
    {
        get => (Color)GetValue(LabelColorProperty);
        set => SetValue(LabelColorProperty, value);
    }

    public Color ValueColor
    {
        get => (Color)GetValue(ValueColorProperty);
        set => SetValue(ValueColorProperty, value);
    }

    public Color TooltipColor
    {
        get => (Color)GetValue(TooltipColorProperty);
        set => SetValue(TooltipColorProperty, value);
    }

    /// <summary>Background of the hovered month's column band.</summary>
    public Color HoverColor
    {
        get => (Color)GetValue(HoverColorProperty);
        set => SetValue(HoverColorProperty, value);
    }

    /// <summary>Text placed before values in the readout, e.g. "MYR ".</summary>
    public string ValuePrefix
    {
        get => (string)GetValue(ValuePrefixProperty);
        set => SetValue(ValuePrefixProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _plot = new RectF(
            dirtyRect.Left + LeftBand,
            dirtyRect.Top + TopPadding,
            Math.Max(0, dirtyRect.Width - LeftBand - RightPadding),
            Math.Max(0, dirtyRect.Height - TopPadding - AxisBand));

        var flows = Flows;
        if (flows is null || flows.Count == 0 || _plot.Width <= 0 || _plot.Height <= 0)
            return;

        var plot = _plot;
        var largest = (double)flows.Max(f => Math.Max(f.Income, f.Expense));
        // A month range with no activity still gets a readable 0–100 money axis.
        var (_, axisMax, step) = ChartScale.Nice(0, Math.Max(largest, 100));
        var slot = plot.Width / flows.Count;
        var barWidth = Math.Min(MaxBarWidth, (slot * 0.56f - PairGap) / 2);
        float Y(double v) => (float)(plot.Bottom - v / axisMax * plot.Height);

        if (_hoverIndex is int hovered && hovered < flows.Count)
        {
            canvas.FillColor = HoverColor;
            canvas.FillRoundedRectangle(plot.Left + hovered * slot + 4, plot.Top, slot - 8, plot.Height, 8);
        }

        canvas.Font = GFont.Default;
        canvas.FontSize = 11;
        canvas.FontColor = LabelColor;
        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        for (var v = 0d; v <= axisMax + step / 2; v += step)
        {
            var y = Y(v);
            canvas.DrawLine(plot.Left, y, plot.Right, y);
            canvas.DrawString(ChartScale.Compact(v), dirtyRect.Left, y - 8, LeftBand - 10, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        for (var i = 0; i < flows.Count; i++)
        {
            var center = plot.Left + slot * (i + 0.5f);
            DrawColumn(canvas, center - PairGap / 2 - barWidth, barWidth, Y((double)flows[i].Income), plot.Bottom, IncomeColor);
            DrawColumn(canvas, center + PairGap / 2, barWidth, Y((double)flows[i].Expense), plot.Bottom, ExpenseColor);

            canvas.FontColor = LabelColor;
            canvas.DrawString(flows[i].MonthStart.ToString("MMM", CultureInfo.CurrentCulture), center - 24, plot.Bottom + 8, 48, 14, HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        if (_hoverIndex is int index && index < flows.Count)
            DrawReadout(canvas, dirtyRect, plot.Left + slot * (index + 0.5f), flows[index]);
    }

    private static void DrawColumn(ICanvas canvas, float x, float width, float top, float bottom, Color color)
    {
        var height = bottom - top;
        if (height <= 0)
            return;

        height = Math.Max(height, 2);
        var radius = Math.Min(4f, Math.Min(height, width / 2));
        var path = new PathF();
        path.AppendRoundedRectangle(x, bottom - height, width, height, radius, radius, 0, 0);
        canvas.FillColor = color;
        canvas.FillPath(path);
    }

    private void DrawReadout(ICanvas canvas, RectF bounds, float centerX, MonthlyFlow flow)
    {
        const float width = 176;
        const float height = 70;

        var left = Math.Clamp(centerX - width / 2, bounds.Left, bounds.Right - width);
        var top = _plot.Top;

        canvas.FillColor = TooltipColor;
        canvas.FillRoundedRectangle(left, top, width, height, 8);
        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(left, top, width, height, 8);

        canvas.Font = GFont.Default;
        canvas.FontSize = 11;
        canvas.FontColor = LabelColor;
        canvas.DrawString(flow.MonthStart.ToString("MMMM yyyy", CultureInfo.CurrentCulture), left + 10, top + 6, width - 20, 14, HorizontalAlignment.Left, VerticalAlignment.Center);

        DrawReadoutRow(canvas, left, top + 24, width, IncomeColor, flow.Income, "Income");
        DrawReadoutRow(canvas, left, top + 44, width, ExpenseColor, flow.Expense, "Spent");
    }

    // Each row keys its series with a short stroke; the value leads and the label follows.
    private void DrawReadoutRow(ICanvas canvas, float left, float top, float width, Color key, decimal value, string label)
    {
        canvas.StrokeColor = key;
        canvas.StrokeSize = 2;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawLine(left + 10, top + 8, left + 20, top + 8);

        canvas.Font = GFont.DefaultBold;
        canvas.FontSize = 12;
        canvas.FontColor = ValueColor;
        canvas.DrawString(ValuePrefix + value.ToString("N2", CultureInfo.CurrentCulture), left + 28, top, width - 90, 16, HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.Font = GFont.Default;
        canvas.FontSize = 11;
        canvas.FontColor = LabelColor;
        canvas.DrawString(label, left + width - 60, top, 50, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
    }

    private void SetHover(Point? position)
    {
        var count = Flows?.Count ?? 0;
        int? index = null;

        if (position is Point p && count > 0 && _plot.Width > 0 && p.X >= _plot.Left && p.X <= _plot.Right)
            index = (int)Math.Clamp((p.X - _plot.Left) / (_plot.Width / count), 0, count - 1);

        if (index == _hoverIndex)
            return;

        _hoverIndex = index;
        Invalidate();
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue)
        => ((FlowBarChart)bindable).Invalidate();
}
