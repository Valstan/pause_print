using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PausePrint.Interop;

public static class WinSpool
{
    private const string WinspoolDll = "winspool.drv";

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport(WinspoolDll, SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool GetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint cbBuf, out uint pcbNeeded);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint command);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindFirstPrinterChangeNotification(IntPtr hPrinter, uint fdwFlags, uint fdwOptions, IntPtr pPrinterNotifyOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindNextPrinterChangeNotification(IntPtr hChange, out uint pdwChange, IntPtr pPrinterNotifyOptions, out IntPtr ppPrinterNotifyInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool FindCloseChangeNotification(IntPtr hChange);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumPrinters(uint Flags, string? Name, uint Level, IntPtr pPrinterEnum, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumJobs(IntPtr hPrinter, uint FirstJob, uint NoJobs, uint Level, IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    public const uint PRINTER_CHANGE_ADD_JOB = 0x00000100;
    public const uint PRINTER_CHANGE_SET_JOB = 0x00000200;
    public const uint PRINTER_CHANGE_DELETE_JOB = 0x00000400;
    public const uint PRINTER_CHANGE_WRITE_JOB = 0x00001000;
    public const uint PRINTER_CHANGE_ALL = 0x7777FFFF;

    public const uint PRINTER_ENUM_LOCAL = 0x00000002;
    public const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;

    public const uint JOB_CONTROL_PAUSE = 1;
    public const uint JOB_CONTROL_RESUME = 2;
    public const uint JOB_CONTROL_CANCEL = 3;

    public static void ThrowIfWin32False(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}


