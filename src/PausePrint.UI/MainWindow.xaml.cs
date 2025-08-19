using System.Collections.ObjectModel;
using System.Windows;
using PausePrint.Interop;
using PausePrint.UI.Ipc;

namespace PausePrint.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PrintJobDto> _jobs = new();
    private NamedPipeClient? _client;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _jobs;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var cts = new System.Threading.CancellationTokenSource();
        _client = new NamedPipeClient(_jobs, cts.Token);
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_client != null) await _client.DisposeAsync();
    }
}