using System.Windows;
using System.Windows.Controls;
using Vizstrap.ViewModels;

namespace Vizstrap.Views.SettingsPages;

public partial class PlaytimePage : Page
{
    public PlaytimePage()
    {
        InitializeComponent();
    }

    // the history (and Roblox's logs) is read only when the page is looked at
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
            await settings.Playtime.EnsureLoadedAsync();
    }
}
