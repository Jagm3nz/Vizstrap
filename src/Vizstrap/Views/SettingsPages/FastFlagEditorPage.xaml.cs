using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Vizstrap.Localization;
using Vizstrap.ViewModels;

namespace Vizstrap.Views.SettingsPages;

public partial class FastFlagEditorPage : Page
{
    public FastFlagEditorPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Engine.Rows.CollectionChanged += OnRowsChanged;
            UpdateEmptyText();
        };
        Unloaded += (_, _) => Engine.Rows.CollectionChanged -= OnRowsChanged;
    }

    private EngineSettingsViewModel Engine => ((SettingsViewModel)DataContext).Engine;

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyText();

    private void UpdateEmptyText() => EmptyText.Visibility = Engine.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnBack(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as SettingsWindow)?.NavigateTo(typeof(EngineSettingsPage));

    private void OnAdd(object sender, RoutedEventArgs e) =>
        FlagDialog.ShowAdd(Window.GetWindow(this), Engine.AddFlag);

    private void OnImport(object sender, RoutedEventArgs e) =>
        FlagDialog.ShowImport(Window.GetWindow(this), Engine.ImportJson);

    private void OnDelete(object sender, RoutedEventArgs e) =>
        Engine.Delete(Grid.SelectedItems.Cast<FlagRow>());

    private void OnExport(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Engine.ExportJson());
            MessageWindow.ShowInfo(Strings.Flags_Exported);
        }
        catch (Exception ex)
        {
            // the clipboard can be held by another app
            MessageWindow.ShowError(Strings.Error_Unexpected, ex);
        }
    }
}
