using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PausePrint.Interop;
using PausePrint.Service.Ipc;

namespace PausePrint.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { });

    private record PrintJobEvent(string PrinterName, uint JobId, string UserName, string DocumentName);
    private readonly Channel<PrintJobEvent> _events = Channel.CreateUnbounded<PrintJobEvent>();
    private readonly Channel<PrintJobDto> _outgoingUi = Channel.CreateUnbounded<PrintJobDto>();

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PausePrint.Service starting...");
        await using var ipc = new NamedPipeServer(_loggerFactory.CreateLogger<NamedPipeServer>(), _outgoingUi, stoppingToken);
        var monitorTask = Task.Run(() => MonitorPrintersAsync(stoppingToken), stoppingToken);
        var processTask = Task.Run(() => ProcessEventsAsync(stoppingToken), stoppingToken);
        await Task.WhenAll(monitorTask, processTask);
    }

    private async Task ProcessEventsAsync(CancellationToken token)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(token))
        {
            _logger.LogInformation("ADD_JOB: {printer} JobId={jobId} User={user} Doc={doc}", evt.PrinterName, evt.JobId, evt.UserName, evt.DocumentName);
            await _outgoingUi.Writer.WriteAsync(new PrintJobDto(evt.PrinterName, evt.JobId, evt.UserName, evt.DocumentName), token);
        }
    }

    private void MonitorPrintersAsync(CancellationToken token)
    {
        foreach (var printer in EnumeratePrinters())
        {
            if (token.IsCancellationRequested) break;
            Task.Run(() => MonitorPrinterAsync(printer, token), token);
        }
    }

    private IEnumerable<string> EnumeratePrinters()
    {
        var flags = WinSpool.PRINTER_ENUM_LOCAL | WinSpool.PRINTER_ENUM_CONNECTIONS;
        WinSpool.EnumPrinters(flags, null, 1, IntPtr.Zero, 0, out var needed, out var returned);
        if (needed == 0 || returned == 0)
        {
            return Array.Empty<string>();
        }
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!WinSpool.EnumPrinters(flags, null, 1, buffer, needed, out needed, out returned))
            {
                _logger.LogWarning("EnumPrinters failed: {err}", Marshal.GetLastWin32Error());
                return Array.Empty<string>();
            }
            var result = new List<string>();
            var ptr = buffer;
            var size = Marshal.SizeOf<PRINTER_INFO_1>();
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<PRINTER_INFO_1>(ptr);
                ptr += size;
                if (info.pName != IntPtr.Zero)
                {
                    var name = Marshal.PtrToStringUni(info.pName);
                    if (!string.IsNullOrWhiteSpace(name)) result.Add(name);
                }
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void MonitorPrinterAsync(string printerName, CancellationToken token)
    {
        _logger.LogInformation("Monitoring: {printer}", printerName);
        if (!WinSpool.OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
        {
            _logger.LogWarning("OpenPrinter failed {printer}: {err}", printerName, Marshal.GetLastWin32Error());
            return;
        }
        try
        {
            var change = WinSpool.FindFirstPrinterChangeNotification(hPrinter, WinSpool.PRINTER_CHANGE_ADD_JOB | WinSpool.PRINTER_CHANGE_SET_JOB, 0, IntPtr.Zero);
            if (change == IntPtr.Zero)
            {
                _logger.LogWarning("FindFirstPrinterChangeNotification failed {printer}: {err}", printerName, Marshal.GetLastWin32Error());
                return;
            }
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Wait using Win32 wait on the notification handle via EventWaitHandle wrapper
                    using var safeHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(change, ownsHandle: false);
                    using var wait = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset) { SafeWaitHandle = safeHandle };
                    var signaled = wait.WaitOne(TimeSpan.FromSeconds(5));
                    if (!signaled) continue;
                    if (!WinSpool.FindNextPrinterChangeNotification(change, out var reason, IntPtr.Zero, out _))
                    {
                        _logger.LogWarning("FindNextPrinterChangeNotification failed: {err}", Marshal.GetLastWin32Error());
                        continue;
                    }
                    if ((reason & WinSpool.PRINTER_CHANGE_ADD_JOB) != 0)
                    {
                        // Enumerate recent jobs to capture metadata (POC: get the first one)
                        TryPublishLatestJobEvent(printerName);
                    }
                }
            }
            finally
            {
                WinSpool.FindCloseChangeNotification(change);
            }
        }
        finally
        {
            WinSpool.ClosePrinter(hPrinter);
        }
    }

    private void TryPublishLatestJobEvent(string printerName)
    {
        if (!WinSpool.OpenPrinter(printerName, out var hPrinter, IntPtr.Zero)) return;
        try
        {
            WinSpool.EnumJobs(hPrinter, 0, 1, 1, IntPtr.Zero, 0, out var needed, out var returned);
            if (needed == 0 || returned == 0) return;
            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!WinSpool.EnumJobs(hPrinter, 0, 1, 1, buffer, needed, out _, out returned) || returned == 0) return;
                var job1 = Marshal.PtrToStructure<JOB_INFO_1>(buffer);
                var user = InteropHelpers.PtrToString(job1.pUserName);
                var doc = InteropHelpers.PtrToString(job1.pDocument);
                _events.Writer.TryWrite(new PrintJobEvent(printerName, job1.JobId, user, doc));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            WinSpool.ClosePrinter(hPrinter);
        }
    }
}
