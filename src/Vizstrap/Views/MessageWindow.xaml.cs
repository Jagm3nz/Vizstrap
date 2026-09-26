using System.Diagnostics;
using System.Windows;
using Vizstrap.Core.Logging;
using Vizstrap.Localization;
using Wpf.Ui.Controls;

namespace Vizstrap.Views;

/// <summary>Vizstrap's message box: errors (with copyable details and the log), confirmations and notices.</summary>
public partial class MessageWindow : NeonWindow
{
    private MessageWindow(string heading, string message, string primary, string? secondary, SymbolRegular symbol)
    {
        InitializeComponent();

        HeadingText.Text = heading;
        MessageText.Text = message;
        PrimaryButton.Content = primary;
        Glyph.Symbol = symbol;

        if (secondary is null)
            SecondaryButton.Visibility = Visibility.Collapsed;
        else
            SecondaryButton.Content = secondary;
    }

    public bool PrimaryChosen { get; private set; }

    public static void ShowError(string message, Exception? exception)
    {
        var window = new MessageWindow(Strings.Error_Title, message, Strings.Common_Close, null, SymbolRegular.ErrorCircle24);

        if (exception is not null)
        {
            window.DetailsText.Text = exception.ToString();
            window.DetailsExpander.Visibility = Visibility.Visible;
            window.DiagnosticsButtons.Visibility = Visibility.Visible;
        }

        window.OpenLogButton.Visibility = Log.FilePath is null ? Visibility.Collapsed : Visibility.Visible;
        window.ShowDialog();
    }

    public static void ShowInfo(string message)
    {
        new MessageWindow("Vizstrap", message, Strings.Common_Close, null, SymbolRegular.CheckmarkCircle24).ShowDialog();
    }

    public static bool Confirm(string heading, string message, string primary, string secondary, bool danger = false)
    {
        var window = new MessageWindow(heading, message, primary, secondary, danger ? SymbolRegular.Warning24 : SymbolRegular.Info24);

        if (danger)
            window.PrimaryButton.Appearance = ControlAppearance.Danger;

        window.ShowDialog();
        return window.PrimaryChosen;
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        PrimaryChosen = true;
        Close();
    }

    private void OnSecondary(object sender, RoutedEventArgs e) => Close();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText($"Vizstrap {App.Version}\n{HeadingText.Text}\n{MessageText.Text}\n\n{DetailsText.Text}");
            CopyButton.Content = Strings.Error_Copied;
        }
        catch (Exception ex)
        {
            // the clipboard can be locked by another app
            Log.Warn(nameof(MessageWindow), $"Copy failed: {ex.Message}");
        }
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        string? logFile = Log.FilePath;

        if (logFile is not null && File.Exists(logFile))
            Process.Start(new ProcessStartInfo(logFile) { UseShellExecute = true })?.Dispose();
    }
}
