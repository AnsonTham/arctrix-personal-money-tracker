using System.Globalization;
using Arctrix.PersonalMoneyTracker.Helpers;
using Arctrix.PersonalMoneyTracker.Models;
using GFont = Microsoft.Maui.Graphics.Font;
using HorizontalAlignment = Microsoft.Maui.Graphics.HorizontalAlignment;
using VerticalAlignment = Microsoft.Maui.Graphics.VerticalAlignment;

namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>
/// Single-series area line chart for a value over time (e.g. net worth by month).
/// Mark specs: 2px line, ~10% area wash, solid hairline grid with clean ticks,
/// end dot with a surface ring, and a crosshair readout on hover or tap.
/// </summary>
public class TrendChart : GraphicsView, IDrawable
{
    private const float LeftBand = 44;
    private const float RightPadding = 20;
    private const float TopPadding = 10;
    private const float AxisBand = 24;

    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(TrendChart), null, propertyChanged: OnPointsChanged);

    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor), typeof(Color), typeof(TrendChart), Color.FromArgb("#E6B450"), propertyChanged: Redraw);

    public static readonly BindableProperty SurfaceColorProperty = BindableProperty.Create(
        nameof(SurfaceColor), typeof(Color), typeof(TrendChart), Color.FromArgb("#10141A"), propertyChanged: Redraw);

    public static readonly BindableProperty GridColorProperty = BindableProperty.Create(
        nameof(GridColor), typeof(Color), typeof(TrendChart), Color.FromArgb("#252C35"), propertyChanged: Redraw);

    public static readonly BindableProperty LabelColorProperty = BindableProperty.Create(
        nameof(LabelColor), typeof(Color), typeof(TrendChart), Color.FromArgb("#8F98A7"), propertyChanged: Redraw);

    public static readonly BindableProperty ValueColorProperty = BindableProperty.Create(
        nameof(ValueColor), typeof(Color), typeof(TrendChart), Color.FromArgb("#EEF1F5"), propertyChanged: Redraw);

    public static readonly BindableProperty TooltipColorProperty = BindableProperty.Create(
        nameof(TooltipColor), typeof(Color), typeof(TrendChart), Color.FromArgb("#171C23"), propertyChanged: Redraw);

    public static readonly BindableProperty ValuePrefixProperty = BindableProperty.Create(
        nameof(ValuePrefix), typeof(string), typeof(TrendChart), string.Empty, propertyChanged: Redraw);

    private const string RevealAnimation = "reveal";

    private RectF _plot;
    private int? _hoverIndex;

    // 0 to 1 while new data rises from the baseline; the grid and labels don't animate.
    private float _reveal = 1;

    public TrendChart()
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

    public IReadOnlyList<ChartPoint>? Points
    {
        get => (IReadOnlyList<ChartPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    /// <summary>Color of the card behind the chart; used for the ring around dots.</summary>
    public Color SurfaceColor
    {
        get => (Color)GetValue(SurfaceColorProperty);
        set => SetValue(SurfaceColorProperty, value);
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

        var points = Points;
        if (points is null || points.Count == 0 || _plot.Width <= 0 || _plot.Height <= 0)
            return;

        var (axisMin, axisMax, step) = ChartScale.Nice(points.Min(p => p.Value), points.Max(p => p.Value));
        var plot = _plot;
        float X(int i) => points.Count == 1 ? plot.Center.X : plot.Left + i * plot.Width / (points.Count - 1);
        float Y(double v) => (float)(plot.Bottom - (v - axisMin) / (axisMax - axisMin) * plot.Height);
        var reveal = _reveal;
        float DataY(double v) => plot.Bottom - (plot.Bottom - Y(v)) * reveal;

        canvas.Font = GFont.Default;
        canvas.FontSize = 11;
        canvas.FontColor = LabelColor;

        // Recessive hairline grid with clean, compact tick labels.
        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        for (var v = axisMin; v <= axisMax + step / 2; v += step)
        {
            var y = Y(v);
            canvas.DrawLine(plot.Left, y, plot.Right, y);
            canvas.DrawString(ChartScale.Compact(v, step),dirtyRect.Left, y - 8, LeftBand - 10, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        for (var i = 0; i < points.Count; i++)
            canvas.DrawString(points[i].Label, X(i) - 20, plot.Bottom + 8, 40, 14, HorizontalAlignment.Center, VerticalAlignment.Top);

        var line = new PathF();
        var area = new PathF();
        for (var i = 0; i < points.Count; i++)
        {
            var (x, y) = (X(i), DataY(points[i].Value));
            if (i == 0)
            {
                line.MoveTo(x, y);
                area.MoveTo(x, y);
            }
            else
            {
                line.LineTo(x, y);
                area.LineTo(x, y);
            }
        }
        area.LineTo(X(points.Count - 1), plot.Bottom);
        area.LineTo(X(0), plot.Bottom);
        area.Close();

        canvas.FillColor = LineColor.WithAlpha(0.10f * reveal);
        canvas.FillPath(area);

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 2;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(line);

        var last = points.Count - 1;
        DrawDot(canvas, X(last), DataY(points[last].Value));

        if (_hoverIndex is int hover && hover < points.Count)
        {
            var hx = X(hover);
            var hy = DataY(points[hover].Value);

            canvas.StrokeColor = LabelColor.WithAlpha(0.5f);
            canvas.StrokeSize = 1;
            canvas.DrawLine(hx, plot.Top, hx, plot.Bottom);
            DrawDot(canvas, hx, hy);
            DrawReadout(canvas, dirtyRect, hx, hy, points[hover]);
        }
    }

    private void DrawDot(ICanvas canvas, float x, float y)
    {
        canvas.FillColor = SurfaceColor;
        canvas.FillCircle(x, y, 6);
        canvas.FillColor = LineColor;
        canvas.FillCircle(x, y, 4);
    }

    private void DrawReadout(ICanvas canvas, RectF bounds, float x, float y, ChartPoint point)
    {
        const float width = 136;
        const float height = 44;
        const float offset = 14;

        var left = Math.Clamp(x - width / 2, bounds.Left, bounds.Right - width);
        var top = y - height - offset >= bounds.Top ? y - height - offset : y + offset;

        canvas.FillColor = TooltipColor;
        canvas.FillRoundedRectangle(left, top, width, height, 8);
        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(left, top, width, height, 8);

        // Value leads, label follows.
        canvas.Font = GFont.DefaultBold;
        canvas.FontSize = 13;
        canvas.FontColor = ValueColor;
        canvas.DrawString(ValuePrefix + point.Value.ToString("N2", CultureInfo.CurrentCulture), left + 10, top + 6, width - 20, 18, HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.Font = GFont.Default;
        canvas.FontSize = 11;
        canvas.FontColor = LabelColor;
        canvas.DrawString(point.Label, left + 10, top + 24, width - 20, 14, HorizontalAlignment.Left, VerticalAlignment.Center);
    }

    private void SetHover(Point? position)
    {
        var count = Points?.Count ?? 0;
        int? index = null;

        if (position is Point p && count > 0 && _plot.Width > 0)
        {
            index = count == 1
                ? 0
                : (int)Math.Clamp(Math.Round((p.X - _plot.Left) / (_plot.Width / (count - 1))), 0, count - 1);
        }

        if (index == _hoverIndex)
            return;

        _hoverIndex = index;
        Invalidate();
    }

    // Grow the line in only when the data really changed, so revisiting a page doesn't replay it.
    private static void OnPointsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var chart = (TrendChart)bindable;
        if (newValue is IReadOnlyList<ChartPoint> { Count: > 0 } points
            && (oldValue is not IReadOnlyList<ChartPoint> previous || !previous.SequenceEqual(points)))
            chart.PlayReveal();
        else
            chart.Invalidate();
    }

    /// <summary>Stops the reveal when the chart leaves the screen; its window may be torn down next.</summary>
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);
        if (args.NewHandler is null)
        {
            this.AbortAnimation(RevealAnimation);
            _reveal = 1;
        }
    }

    private void PlayReveal()
    {
        this.AbortAnimation(RevealAnimation);
        if (Handler is null)
        {
            _reveal = 1;
            Invalidate();
            return;
        }

        _reveal = 0;
        try
        {
            new Animation(progress =>
                {
                    _reveal = (float)progress;
                    Invalidate();
                })
                .Commit(this, RevealAnimation, 16, Motion.Long, Motion.Ease, (_, _) =>
                {
                    _reveal = 1;
                    Invalidate();
                });
        }
        catch (ObjectDisposedException)
        {
            // The window was torn down as the data arrived; draw the finished chart.
            _reveal = 1;
            Invalidate();
        }
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue)
        => ((TrendChart)bindable).Invalidate();
}
