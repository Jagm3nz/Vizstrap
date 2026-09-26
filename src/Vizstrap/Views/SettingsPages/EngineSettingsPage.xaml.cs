using System.Windows;
using System.Windows.Controls;

namespace Vizstrap.Views.SettingsPages;

public partial class EngineSettingsPage : Page
{
    public EngineSettingsPage()
    {
        InitializeComponent();
    }

    private void OnOpenEditor(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as SettingsWindow)?.NavigateTo(typeof(FastFlagEditorPage));
}
