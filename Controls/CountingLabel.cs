using System.Globalization;
using Arctrix.PersonalMoneyTracker.Helpers;

namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>
/// A money label that counts to a new value instead of snapping to it. An optional prefix
/// (such as the currency code) is drawn as a separately styled span before the number.
/// </summary>
public class CountingLabel : Label
{
    private const string CountAnimation = "count";

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(decimal), typeof(CountingLabel), 0m,
        propertyChanged: (bindable, _, newValue) => ((CountingLabel)bindable).CountTo((decimal)newValue));

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(CountingLabel), "N2", propertyChanged: Rerender);

    public static readonly BindableProperty PrefixProperty = BindableProperty.Create(
        nameof(Prefix), typeof(string), typeof(CountingLabel), string.Empty, propertyChanged: Rerender);

    public static readonly BindableProperty PrefixFontSizeProperty = BindableProperty.Create(
        nameof(PrefixFontSize), typeof(double), typeof(CountingLabel), 0d, propertyChanged: Rerender);

    public static readonly BindableProperty PrefixFontFamilyProperty = BindableProperty.Create(
        nameof(PrefixFontFamily), typeof(string), typeof(CountingLabel), null, propertyChanged: Rerender);

    public static readonly BindableProperty PrefixColorProperty = BindableProperty.Create(
        nameof(PrefixColor), typeof(Color), typeof(CountingLabel), null, propertyChanged: Rerender);

    private decimal _shown;

    public CountingLabel() => Render(0);

    public decimal Value
    {
        get => (decimal)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Numeric format string for the value; defaults to two decimals with grouping.</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public string Prefix
    {
        get => (string)GetValue(PrefixProperty);
        set => SetValue(PrefixProperty, value);
    }

    /// <summary>Font size for the prefix; 0 uses the label's size.</summary>
    public double PrefixFontSize
    {
        get => (double)GetValue(PrefixFontSizeProperty);
        set => SetValue(PrefixFontSizeProperty, value);
    }

    public string? PrefixFontFamily
    {
        get => (string?)GetValue(PrefixFontFamilyProperty);
        set => SetValue(PrefixFontFamilyProperty, value);
    }

    public Color? PrefixColor
    {
        get => (Color?)GetValue(PrefixColorProperty);
        set => SetValue(PrefixColorProperty, value);
    }

    private void CountTo(decimal target)
    {
        this.AbortAnimation(CountAnimation);

        // Without a platform view there is nothing to animate on; show the value directly.
        var from = _shown;
        if (Handler is null || from == target)
        {
            Render(target);
            return;
        }

        new Animation(progress => Render(from + (target - from) * (decimal)progress))
            .Commit(this, CountAnimation, 16, Motion.Long, Motion.Ease, (_, cancelled) =>
            {
                if (!cancelled)
                    Render(target);
            });
    }

    private void Render(decimal value)
    {
        _shown = value;
        var number = value.ToString(Format, CultureInfo.CurrentCulture);

        if (string.IsNullOrEmpty(Prefix))
        {
            FormattedText = null;
            Text = number;
            return;
        }

        var prefix = new Span { Text = Prefix + " " };
        if (PrefixFontSize > 0)
            prefix.FontSize = PrefixFontSize;
        prefix.FontFamily = PrefixFontFamily ?? FontFamily;
        if (PrefixColor is not null)
            prefix.TextColor = PrefixColor;

        // Spans don't pick up the label's styled font family, so carry it over explicitly.
        var formatted = new FormattedString();
        formatted.Spans.Add(prefix);
        formatted.Spans.Add(new Span { Text = number, FontFamily = FontFamily });
        FormattedText = formatted;
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == FontFamilyProperty.PropertyName && !string.IsNullOrEmpty(Prefix))
            Render(_shown);
    }

    private static void Rerender(BindableObject bindable, object oldValue, object newValue)
    {
        var label = (CountingLabel)bindable;
        label.Render(label._shown);
    }
}
