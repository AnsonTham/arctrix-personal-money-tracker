using Arctrix.PersonalMoneyTracker.Views;

namespace Arctrix.PersonalMoneyTracker.Controls;

/// <summary>
/// In-page back button for pushed pages. Shows itself only while its page's
/// <see cref="AppPage.ShowBackButton"/> is true, and navigates back on click.
/// </summary>
public class BackButton : Button
{
    private AppPage? _page;

    public BackButton()
    {
        IsVisible = false;
        SemanticProperties.SetDescription(this, "Back");
        Clicked += async (_, _) => await Shell.Current.GoToAsync("..");
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    private void Attach()
    {
        Detach();
        Element? element = Parent;
        while (element is not null and not AppPage)
            element = element.Parent;

        _page = element as AppPage;
        if (_page is null)
            return;

        _page.PropertyChanged += OnPagePropertyChanged;
        IsVisible = _page.ShowBackButton;
    }

    private void Detach()
    {
        if (_page is not null)
            _page.PropertyChanged -= OnPagePropertyChanged;
        _page = null;
    }

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppPage.ShowBackButton) && _page is not null)
            IsVisible = _page.ShowBackButton;
    }
}
