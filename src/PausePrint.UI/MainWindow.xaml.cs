using System.Collections.ObjectModel;
using System.Windows;
using PausePrint.Interop;
using PausePrint.UI.Ipc;
using PausePrint.UI.Services;
using PausePrint.UI.Utils;

namespace PausePrint.UI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PrintJobDto> _jobs = new();
    private PrintMonitorController? _controller;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _jobs;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _controller = new PrintMonitorController();
        _controller.JobReceived += job => Dispatcher.Invoke(() => _jobs.Add(job));
        // Заполним список принтеров для фильтра
        foreach (var p in PrintMonitorController.EnumeratePrinters())
        {
            PrintersCombo.Items.Add(p);
        }
        AutoStartCheck.IsChecked = AutoStart.IsEnabled();
    }

    private async void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_controller != null) await _controller.DisposeAsync();
    }

    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        _controller.AutoHold = AutoHoldCheck.IsChecked == true;
        await _controller.StartAsync();
        StatusText.Text = "Запущено";
        ((System.Windows.Media.SolidColorBrush)FindName("StatusBrush")).Color = System.Windows.Media.Colors.LimeGreen;
    }

    private async void StopClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        await _controller.StopAsync();
        StatusText.Text = "Остановлено";
        ((System.Windows.Media.SolidColorBrush)FindName("StatusBrush")).Color = System.Windows.Media.Colors.Gray;
    }

    private async void ResumeClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        if ((sender as FrameworkElement)?.DataContext is PrintJobDto job)
        {
            await _controller.ResumeJobAsync(job.PrinterName, job.JobId);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        if ((sender as FrameworkElement)?.DataContext is PrintJobDto job)
        {
            _controller.CancelJob(job.PrinterName, job.JobId);
            _jobs.Remove(job);
        }
    }

    private void ApplyThrottleClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        if (int.TryParse(ThrottleK.Text, out var k) && int.TryParse(ThrottleT.Text, out var t))
        {
            _controller.SetThrottle(k, t);
            ApplyJobsHint.Visibility = Visibility.Visible;
            _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => ApplyJobsHint.Visibility = Visibility.Collapsed));
        }
    }

    private void AddPrinterClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        if (PrintersCombo.SelectedItem is string name)
        {
            _controller.SetEnabledPrinters(new[] { name });
        }
    }

    private void ClearPrintersClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        _controller.SetEnabledPrinters(Array.Empty<string>());
    }

    private void ApplyPagesThrottleClick(object sender, RoutedEventArgs e)
    {
        if (_controller == null) return;
        if (int.TryParse(PagesEvery.Text, out var every) && int.TryParse(PagesPauseSec.Text, out var sec))
        {
            _controller.SetPageThrottle(every, sec, PagesPauseEnabled.IsChecked == true);
            ApplyPagesHint.Visibility = Visibility.Visible;
            _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => ApplyPagesHint.Visibility = Visibility.Collapsed));
        }
    }

    private void SaveAutoStartClick(object sender, RoutedEventArgs e)
    {
        AutoStart.SetEnabled(AutoStartCheck.IsChecked == true);
        SavedAutoStartHint.Visibility = Visibility.Visible;
        _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => SavedAutoStartHint.Visibility = Visibility.Collapsed));
    }
}