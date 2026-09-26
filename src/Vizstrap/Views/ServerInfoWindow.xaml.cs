using System.Windows;
using Vizstrap.Core.Activity;
using Vizstrap.Localization;

namespace Vizstrap.Views;

/// <summary>Bloxstrap's "Server information": server type, instance id and, if enabled, location.</summary>
public partial class ServerInfoWindow : NeonWindow
{
    private readonly GameSession _session;

    /// <param name="locator">Null when looking up locations is switched off; the row is hidden then.</param>
    public ServerInfoWindow(GameSession session, ServerLocator? locator)
    {
        InitializeComponent();
        _session = session;

        TypeText.Text = ServerTypeName(session.ServerType);
        InstanceText.Text = session.JobId;

        if (locator is null)
        {
            LocationLabel.Visibility = Visibility.Collapsed;
            LocationText.Visibility = Visibility.Collapsed;
        }
        else
        {
            LocationText.Text = Strings.Server_Loading;
            Loaded += async (_, _) => LocationText.Text = session.HasPublicAddress
                ? await locator.LocateAsync(session.MachineAddress) ?? Strings.Server_NotAvailable
                : Strings.Server_NotAvailable;
        }
    }

    public static string ServerTypeName(ServerType type) => type switch
    {
        ServerType.Private => Strings.Server_Private,
        ServerType.Reserved => Strings.Server_Reserved,
        _ => Strings.Server_Public,
    };

    private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(_session.JobId);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
