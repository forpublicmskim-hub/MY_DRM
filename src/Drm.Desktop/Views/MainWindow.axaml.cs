using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Drm.Desktop.ViewModels;

namespace Drm.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly AsyncShutdownCoordinator _shutdown;
    private bool _closeRequestedAfterShutdown;

    public MainWindow()
    {
        InitializeComponent();
        _shutdown = new AsyncShutdownCoordinator(async () =>
        {
            if (DataContext is MainViewModel viewModel)
                await viewModel.DisposeAsync();
        });
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainViewModel viewModel) await viewModel.InitializeAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _shutdown.IsComplete) return;

        e.Cancel = true;
        if (_closeRequestedAfterShutdown) return;
        _closeRequestedAfterShutdown = true;
        _ = CompleteShutdownAndCloseAsync();
    }

    private async Task CompleteShutdownAndCloseAsync()
    {
        try
        {
            await _shutdown.BeginAsync();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError($"Desktop shutdown cleanup failed: {exception}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(Close, DispatcherPriority.Send);
        }
    }
}

internal sealed class AsyncShutdownCoordinator(Func<ValueTask> cleanup)
{
    private readonly object _gate = new();
    private Task? _task;
    private int _complete;

    public bool IsComplete => Volatile.Read(ref _complete) != 0;

    public Task BeginAsync()
    {
        lock (_gate)
            return _task ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await cleanup();
        }
        finally
        {
            Volatile.Write(ref _complete, 1);
        }
    }
}
