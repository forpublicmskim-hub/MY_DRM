using Avalonia.Controls;
using Drm.PolicyMaker.ViewModels;

namespace Drm.PolicyMaker.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MainViewModel)?.Dispose();
        base.OnClosed(e);
    }
}
