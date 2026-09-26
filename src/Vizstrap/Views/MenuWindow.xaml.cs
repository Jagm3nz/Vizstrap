using System.Diagnostics;
using System.Windows;
using Vizstrap.Core.Logging;
using Vizstrap.Localization;

namespace Vizstrap.Views;

public enum MenuChoice
{
    None,
    Launch,
    Settings,
    About,
}

/// <summary>What the installed Vizstrap shows when opened from the Start menu or desktop.</summary>
public partial class MenuWindow : NeonWindow
{
    public MenuWindow()
    {
        InitializeComponent();
        VersionText.Text = string.Format(Strings.Menu_Version, App.Version);
        Loaded += (_, _) => LaunchCard.Focus();
    }

    public MenuChoice Choice { get; private set; }

    private void OnLaunch(object sender, RoutedEventArgs e) => CloseWith(MenuChoice.Launch);

    private void OnSettings(object sender, RoutedEventArgs e) => CloseWith(MenuChoice.Settings);

    private void OnAbout(object sender, RoutedEventArgs e) => CloseWith(MenuChoice.About);

    private void OnHelp(object sender, RoutedEventArgs e)
    {
        string logs = Path.GetDirectoryName(Log.FilePath) ?? App.Paths.Logs;
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { logs } })?.Dispose();
    }

    private void CloseWith(MenuChoice choice)
    {
        Choice = choice;
        Close();
    }
}
