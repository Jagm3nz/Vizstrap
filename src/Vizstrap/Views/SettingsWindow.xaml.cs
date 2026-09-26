using System.ComponentModel;
using System.Windows;
using Vizstrap.Localization;
using Vizstrap.ViewModels;
using Vizstrap.Views.SettingsPages;
using Wpf.Ui.Abstractions;

namespace Vizstrap.Views;

public partial class SettingsWindow : NeonWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel, Type? startPage = null)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Navigation.SetPageProviderService(new PageProvider(viewModel));
        Loaded += (_, _) => Navigation.Navigate(startPage ?? typeof(IntegrationsPage));

        viewModel.CloseRequested += (_, _) => Close();
    }

    /// <summary>Opens a page, including ones without their own menu entry (the Fast Flag editor).</summary>
    public void NavigateTo(Type page) => Navigation.Navigate(page);

    protected override void OnClosing(CancelEventArgs e)
    {
        bool discardChanges = _viewModel.UninstallRequested || !_viewModel.IsDirty ||
            MessageWindow.Confirm(Strings.Settings_UnsavedTitle, Strings.Settings_UnsavedMessage,
                Strings.Settings_DiscardAndClose, Strings.Common_Cancel);

        e.Cancel = !discardChanges;

        if (discardChanges)
            _viewModel.DiscardPreview();

        base.OnClosing(e);
    }

    /// <summary>Creates each page once and hands it the window's view model.</summary>
    private sealed class PageProvider(object dataContext) : INavigationViewPageProvider
    {
        private readonly Dictionary<Type, object> _pages = new();

        public object? GetPage(Type pageType)
        {
            if (!_pages.TryGetValue(pageType, out var page))
            {
                var element = (FrameworkElement)Activator.CreateInstance(pageType)!;
                element.DataContext = dataContext;
                _pages[pageType] = page = element;
            }

            return page;
        }
    }
}
