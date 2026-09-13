namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>
/// A thin horizontal bar showing a 0–1 share on a track: square at the baseline,
/// 4px rounded data end.
/// </summary>
public class ProportionBar : GraphicsView, IDrawable
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(ProportionBar), 0d, propertyChanged: Redraw);

    public static readonly BindableProperty BarColorProperty = BindableProperty.Create(
        nameof(BarColor), typeof(Color), typeof(ProportionBar), Color.FromArgb("#E6B450"), propertyChanged: Redraw);

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(ProportionBar), Color.FromArgb("#1E242D"), propertyChanged: Redraw);

    public ProportionBar()
    {
        Drawable = this;
        HeightRequest = 8;
    }

    /// <summary>Share to fill, from 0 to 1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color BarColor
    {
        get => (Color)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        var radius = Math.Min(4f, dirtyRect.Height / 2);

        canvas.FillColor = TrackColor;
        canvas.FillPath(RoundedEnd(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height, radius));

        var fraction = (float)Math.Clamp(Value, 0, 1);
        if (fraction <= 0)
            return;

        var width = Math.Max(dirtyRect.Height, dirtyRect.Width * fraction);
        canvas.FillColor = BarColor;
        canvas.FillPath(RoundedEnd(dirtyRect.X, dirtyRect.Y, width, dirtyRect.Height, radius));
    }

    private static PathF RoundedEnd(float x, float y, float width, float height, float radius)
    {
        var path = new PathF();
        path.AppendRoundedRectangle(x, y, width, height, 0, radius, 0, radius);
        return path;
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue)
        => ((ProportionBar)bindable).Invalidate();
}
