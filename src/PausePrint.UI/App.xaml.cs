using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace PausePrint.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }
}

