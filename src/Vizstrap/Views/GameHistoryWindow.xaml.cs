using Vizstrap.ViewModels;

namespace Vizstrap.Views;

public partial class GameHistoryWindow : NeonWindow
{
    public GameHistoryWindow(GameHistoryViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
