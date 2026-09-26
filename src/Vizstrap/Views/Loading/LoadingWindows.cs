using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shell;
using Vizstrap.Core.Appearance;
using Vizstrap.Core.Logging;
using Vizstrap.ViewModels;
using Vizstrap.Views.Loading.XmlTheme;

namespace Vizstrap.Views.Loading;

/// <summary>
/// Creates the loading window for the chosen style. The windows only describe how they look; this class
/// gives them all the same behaviour: run on load, close when done, Alt+F4 cancels, taskbar progress,
/// dragging for borderless styles.
/// </summary>
public static class LoadingWindows
{
    /// <param name="showThemeErrors">
    /// For the preview: let a broken XML theme's error through instead of quietly using Neon, which
    /// is what a launch does so a theme can never keep Roblox from starting.
    /// </param>
    public static Window Create(LoadingViewModel viewModel, bool showThemeErrors = false)
    {
        var appearance = viewModel.Appearance;

        Window window = appearance.Style switch
        {
            LoadingStyle.Custom => new CustomLoadingWindow(appearance.Custom ?? new CustomLoadingTheme()),
            LoadingStyle.XmlTheme => LoadXmlTheme(appearance, showThemeErrors) ?? new NeonLoadingWindow(),
            LoadingStyle.Fluent => new FluentLoadingWindow(),
            LoadingStyle.FluentClassic => new FluentClassicLoadingWindow(),
            LoadingStyle.Compact => new CompactLoadingWindow(),
            LoadingStyle.Roblox2014 => new Roblox2014LoadingWindow(viewModel.IsDark),
            LoadingStyle.Byfron => new ByfronLoadingWindow(viewModel.IsDark),
            LoadingStyle.Legacy2011 => new Legacy2011LoadingWindow(),
            LoadingStyle.Legacy2008 => new Legacy2008LoadingWindow(),
            LoadingStyle.Vista => new VistaLoadingWindow(),
            _ => new NeonLoadingWindow(),
        };

        Attach(window, viewModel);
        return window;
    }

    private static Window? LoadXmlTheme(LoadingAppearance appearance, bool showThemeErrors)
    {
        if (appearance.XmlThemeDirectory is null)
        {
            Log.Warn(nameof(LoadingWindows), "No XML theme chosen; using Neon");
            return null;
        }

        try
        {
            return XmlThemeLoadingWindow.Load(appearance.XmlThemeDirectory, appearance.IsDark);
        }
        catch (XmlThemeException ex) when (!showThemeErrors)
        {
            Log.Warn(nameof(LoadingWindows), $"XML theme {appearance.XmlThemeDirectory} can't be shown ({ex.Message}); using Neon");
            return null;
        }
    }

    private static void Attach(Window window, LoadingViewModel viewModel)
    {
        window.DataContext = viewModel;
        window.Title = viewModel.Title;
        window.Icon = viewModel.Appearance.WindowIcon;

        var taskbar = new TaskbarItemInfo();
        BindingOperations.SetBinding(taskbar, TaskbarItemInfo.ProgressStateProperty,
            new Binding(nameof(LoadingViewModel.TaskbarState)) { Source = viewModel });
        BindingOperations.SetBinding(taskbar, TaskbarItemInfo.ProgressValueProperty,
            new Binding(nameof(LoadingViewModel.Progress)) { Source = viewModel });
        window.TaskbarItemInfo = taskbar;

        window.Loaded += async (_, _) => await viewModel.RunAsync();
        viewModel.Finished += (_, _) => window.Close();

        // Alt+F4 or the close button while working means "cancel"; the window closes once work stops
        window.Closing += (_, e) => OnClosing(viewModel, e);

        // styles without a system title bar can be dragged anywhere
        if (window.WindowStyle == WindowStyle.None || window is NeonWindow)
            window.MouseLeftButtonDown += (_, e) => DragMove(window, e);
    }

    private static void OnClosing(LoadingViewModel viewModel, CancelEventArgs e)
    {
        if (!viewModel.IsRunning)
            return;

        e.Cancel = true;

        if (viewModel.CancelCommand.CanExecute(null))
            viewModel.CancelCommand.Execute(null);
    }

    private static void DragMove(Window window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase)
            return;

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // the button was released before dragging started
        }
    }
}
