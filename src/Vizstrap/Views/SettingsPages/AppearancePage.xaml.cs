using System.Windows;
using System.Windows.Controls;
using Vizstrap.ViewModels;
using Vizstrap.Localization;
using Vizstrap.Views.Loading;
using Vizstrap.Views.Loading.XmlTheme;

namespace Vizstrap.Views.SettingsPages;

public partial class AppearancePage : Page
{
    public AppearancePage()
    {
        InitializeComponent();
    }

    /// <summary>Shows the loading window with the current, unsaved choices; nothing is downloaded or launched.</summary>
    private void OnPreview(object sender, RoutedEventArgs e)
    {
        var settings = (SettingsViewModel)DataContext;
        Window window;

        try
        {
            window = LoadingWindows.Create(LoadingViewModel.CreatePreview(settings.PreviewAppearance), showThemeErrors: true);
        }
        catch (XmlThemeException ex)
        {
            // a launch would quietly use Neon; here the theme's author wants to know what's wrong
            MessageWindow.ShowError($"{Strings.XmlThemes_Invalid}\n\n{ex.Message}", null);
            return;
        }

        window.Owner = Window.GetWindow(this);
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.ShowDialog();
    }
}
