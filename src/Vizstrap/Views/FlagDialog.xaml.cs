using System.Windows;
using System.Windows.Controls;
using Vizstrap.Core.FastFlags;
using Vizstrap.Localization;

namespace Vizstrap.Views;

/// <summary>Adds one Fast Flag or imports a JSON object of them; stays open while the input is refused.</summary>
public partial class FlagDialog : NeonWindow
{
    private readonly Func<string?> _confirm;

    private FlagDialog(Window? owner, string title, string confirmText, bool import, Func<FlagDialog, string?> confirm)
    {
        InitializeComponent();

        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Title = title;
        TitleBar.Title = title;
        ConfirmButton.Content = confirmText;
        _confirm = () => confirm(this);

        if (import)
        {
            AddPanel.Visibility = Visibility.Collapsed;
            ImportPanel.Visibility = Visibility.Visible;
            Loaded += (_, _) => JsonBox.Focus();
        }
        else
        {
            NameBox.ItemsSource = FastFlagAllowlist.All.Select(flag => flag.Name).ToList();
            Loaded += (_, _) => NameBox.Focus();
        }
    }

    /// <param name="add">Gets name and value; returns why they were refused, or null.</param>
    public static void ShowAdd(Window? owner, Func<string, string, string?> add) =>
        new FlagDialog(owner, Strings.Flags_Add, Strings.Flags_Add, import: false,
            dialog => add(dialog.NameBox.Text, dialog.ValueBox.Text)).ShowDialog();

    /// <param name="import">Gets the JSON; returns why it was refused, or null.</param>
    public static void ShowImport(Window? owner, Func<string, string?> import) =>
        new FlagDialog(owner, Strings.Flags_Import, Strings.Flags_Import, import: true,
            dialog => import(dialog.JsonBox.Text)).ShowDialog();

    /// <summary>Picking an allowlisted flag fills in Roblox's default as a starting value.</summary>
    private void OnNameChosen(object sender, SelectionChangedEventArgs e)
    {
        if (NameBox.SelectedItem is string name && FastFlagAllowlist.Find(name) is { } flag)
            ValueBox.Text = flag.DefaultValue;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        string? problem = _confirm();

        if (problem is null)
        {
            Close();
            return;
        }

        ErrorText.Text = problem;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
