using Vizstrap.ViewModels;

namespace Vizstrap.Views;

public partial class WelcomeWindow : NeonWindow
{
    public WelcomeWindow(WelcomeViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        viewModel.CloseRequested += (_, _) => Close();
    }
}
