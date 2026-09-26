using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace Vizstrap.Views;

/// <summary>Base for every themed Vizstrap window: borderless Fluent chrome on the Neon background.</summary>
public class NeonWindow : FluentWindow
{
    public NeonWindow()
    {
        SetResourceReference(BackgroundProperty, "NeonBackgroundBrush");

        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WindowBackdropType.None;
        WindowCornerPreference = WindowCornerPreference.Round;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Vizstrap";

        // WPF-UI's window style has a minimum size that defeats SizeToContent on small dialogs
        MinWidth = 0;
        MinHeight = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        // after InitializeComponent, so a window's own resources (e.g. the always-dark loading window) count
        RefreshBackground();
        base.OnSourceInitialized(e);
    }

    /// <summary>
    /// WPF-UI repaints the window from its *own* "ApplicationBackgroundBrush" resource and falls back to
    /// grey #202020 when it's missing, so hand it the Neon background this window currently resolves to.
    /// </summary>
    public void RefreshBackground()
    {
        if (TryFindResource("NeonBackgroundBrush") is Brush background)
            Resources["ApplicationBackgroundBrush"] = background;

        SetResourceReference(BackgroundProperty, "NeonBackgroundBrush");
    }
}
