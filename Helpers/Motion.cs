namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>
/// The app's motion vocabulary: short (150–250ms), eased out, and cheap. Only opacity,
/// translation and scale are animated, so motion never forces a layout pass.
/// </summary>
public static class Motion
{
    public const uint Short = 150;
    public const uint Medium = 220;
    public const uint Long = 250;

    public static readonly Easing Ease = Easing.CubicOut;

    private const double EntranceOffset = 12;
    private const int EntranceStagger = 40;
    private const double PressedScale = 0.97;

    private static readonly BindableProperty HasPressFeedbackProperty = BindableProperty.CreateAttached(
        "HasPressFeedback", typeof(bool), typeof(Motion), false);

    /// <summary>Fades an element in from transparent.</summary>
    public static Task FadeInAsync(VisualElement element)
    {
        element.Opacity = 0;
        return element.FadeToAsync(1, Medium, Ease);
    }

    /// <summary>Hides elements ahead of <see cref="RevealAsync"/>: transparent and shifted down slightly.</summary>
    public static void Hide(IEnumerable<VisualElement> elements)
    {
        foreach (var element in elements)
        {
            element.Opacity = 0;
            element.TranslationY = EntranceOffset;
        }
    }

    /// <summary>Fades and slides hidden elements into place, one after another.</summary>
    public static Task RevealAsync(IReadOnlyList<VisualElement> elements) =>
        Task.WhenAll(elements.Select((element, index) => RevealOneAsync(element, index * EntranceStagger)));

    /// <summary>Gives a button a subtle shrink while pressed. Safe to call more than once.</summary>
    public static void AddPressFeedback(Button button)
    {
        if ((bool)button.GetValue(HasPressFeedbackProperty))
            return;

        button.SetValue(HasPressFeedbackProperty, true);
        button.Pressed += (_, _) => _ = button.ScaleToAsync(PressedScale, Short, Ease);
        button.Released += (_, _) => _ = button.ScaleToAsync(1, Short, Ease);
    }

    private static async Task RevealOneAsync(VisualElement element, int delay)
    {
        if (delay > 0)
            await Task.Delay(delay);

        await Task.WhenAll(
            element.FadeToAsync(1, Medium, Ease),
            element.TranslateToAsync(0, 0, Medium, Ease));
    }
}
