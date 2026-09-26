using Vizstrap.ViewModels;

namespace Vizstrap.Views;

public partial class InstallerWindow : NeonWindow
{
    public InstallerWindow(InstallerViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        EventHandler close = (_, _) => Close();
        viewModel.CloseRequested += close;
        Closed += (_, _) => viewModel.CloseRequested -= close;
    }
}
