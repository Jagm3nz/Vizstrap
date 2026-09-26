namespace Vizstrap.Views.Loading;

/// <summary>Vizstrap's own style: always dark, big icon, speed and package counter.</summary>
public partial class NeonLoadingWindow : NeonWindow
{
    public NeonLoadingWindow()
    {
        InitializeComponent();

        // always the dark palette, in the chosen accent
        Resources = App.CreatePalette(dark: true, App.Accent);
    }
}
