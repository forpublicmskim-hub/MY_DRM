using Avalonia.Controls;
using Drm.Desktop.ViewModels;

namespace Drm.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainViewModel viewModel) await viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnClosed(e);
    }
}
