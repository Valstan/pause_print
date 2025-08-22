using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Serilog;

namespace PausePrint.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _tray;
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var logDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "PausePrint", "logs");
        System.IO.Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(System.IO.Path.Combine(logDir, "ui-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, shared: true)
            .CreateLogger();
        Log.Information("App starting");
        _mutex = new Mutex(initiallyOwned: true, name: "Global\\PausePrint_UI_Mutex", out var createdNew);
        if (!createdNew)
        {
            Log.Warning("Second instance detected, exiting");
            System.Windows.MessageBox.Show("PausePrint уже запущен.", "PausePrint", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _tray = new NotifyIcon
        {
            Text = "PausePrint",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _tray.ContextMenuStrip!.Items.Add("Показать окно", null, (_, _) => ShowMain());
        _tray.ContextMenuStrip.Items.Add("Выход", null, (_, _) => Shutdown());
        ShowMain();
    }

    private void ShowMain()
    {
        if (Current.MainWindow is null)
        {
            Current.MainWindow = new MainWindow();
        }
        Current.MainWindow.Show();
        Current.MainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("App exiting");
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

