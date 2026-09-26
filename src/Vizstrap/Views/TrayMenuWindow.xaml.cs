using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vizstrap.Core.Activity;
using Vizstrap.Integrations;
using Forms = System.Windows.Forms;

namespace Vizstrap.Views;

/// <summary>
/// Vizstrap's icon in the notification area while Roblox runs, with Bloxstrap's menu. The window
/// itself is invisible; it hosts the menu so it gets Vizstrap's look and closes on outside clicks.
/// </summary>
public partial class TrayMenuWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x80;

    private readonly Watcher _watcher;
    private readonly Forms.NotifyIcon _notifyIcon;
    private Action? _notificationClick;

    internal TrayMenuWindow(Watcher watcher)
    {
        InitializeComponent();
        _watcher = watcher;

        VersionText.Text = $"Vizstrap {App.Version}";
        RichPresenceItem.Visibility = watcher.HasRichPresence ? Visibility.Visible : Visibility.Collapsed;
        GameHistoryItem.Visibility = watcher.HasGameHistory ? Visibility.Visible : Visibility.Collapsed;
        OpenLogItem.Visibility = watcher.LogFile is not null ? Visibility.Visible : Visibility.Collapsed;

        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Vizstrap.ico"))!.Stream;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize),
            Text = "Vizstrap",
            Visible = true,
        };

        _notifyIcon.MouseClick += (_, _) => ShowMenu();
        _notifyIcon.BalloonTipClicked += (_, _) => _notificationClick?.Invoke();
        _notifyIcon.BalloonTipClosed += (_, _) => _notificationClick = null;
    }

    public void ShowMenu()
    {
        Activate();
        Menu.IsOpen = true;
    }

    internal void OnGameJoined(GameSession session)
    {
        CopyInviteItem.Visibility = session.ServerType == ServerType.Public ? Visibility.Visible : Visibility.Collapsed;
        ServerInfoItem.Visibility = Visibility.Visible;
    }

    internal void OnGameLeft()
    {
        CopyInviteItem.Visibility = Visibility.Collapsed;
        ServerInfoItem.Visibility = Visibility.Collapsed;
    }

    /// <summary>A Windows notification from the tray icon; clicking it runs <paramref name="onClick"/>.</summary>
    internal void Notify(string title, string text, Action? onClick)
    {
        _notificationClick = onClick;
        _notifyIcon.ShowBalloonTip(10_000, title, text, Forms.ToolTipIcon.None);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // a tool window stays out of Alt+Tab
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExToolWindow);
    }

    protected override void OnClosed(EventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.OnClosed(e);
    }

    private void OnRichPresenceClick(object sender, RoutedEventArgs e) => _watcher.SetRichPresenceVisible(RichPresenceItem.IsChecked);

    private void OnCopyInviteClick(object sender, RoutedEventArgs e) => _watcher.CopyInviteLink();

    private void OnServerInfoClick(object sender, RoutedEventArgs e) => _watcher.ShowServerInfo();

    private void OnGameHistoryClick(object sender, RoutedEventArgs e) => _watcher.ShowGameHistory();

    private void OnCloseRobloxClick(object sender, RoutedEventArgs e) => _watcher.CloseRoblox();

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => _watcher.OpenLog();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
