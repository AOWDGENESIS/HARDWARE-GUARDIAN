using System.Windows;
using WindowsMaintenanceCenter.App.ViewModels;

namespace WindowsMaintenanceCenter.App.Views;

/// <summary>
/// The shell window. It has no logic beyond wiring the view model and closing cleanly; every action
/// goes through the view model and the services behind it.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
