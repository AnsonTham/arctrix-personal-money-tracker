namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>
/// Hosts a Picker or DatePicker and, on Android, overlays a dropdown (or calendar) glyph so the
/// field reads as something to tap. WinUI draws its own glyphs, so nothing is added there.
/// </summary>
public class PickerField : Grid
{
    public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
        nameof(Glyph), typeof(string), typeof(PickerField), "icon_chevron_down.png",
        propertyChanged: (bindable, _, newValue) =>
        {
            if (((PickerField)bindable)._glyph is Image glyph)
                glyph.Source = (string)newValue;
        });

    private readonly Image? _glyph;

    public PickerField()
    {
        if (DeviceInfo.Current.Platform != DevicePlatform.Android)
            return;

        // Drawn above the picker but transparent to input, so a tap on the glyph still opens it.
        _glyph = new Image
        {
            Source = Glyph,
            WidthRequest = 18,
            HeightRequest = 18,
            Margin = new Thickness(0, 0, 4, 0),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            ZIndex = 1
        };
        Children.Add(_glyph);
    }

    /// <summary>Image file for the glyph; defaults to a chevron.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
