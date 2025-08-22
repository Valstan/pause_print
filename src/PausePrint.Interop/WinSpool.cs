using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PausePrint.Interop;

public static class WinSpool
{
    private const string WinspoolDll = "winspool.drv";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PRINTER_DEFAULTS
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public uint DesiredAccess;
    }

    public const uint PRINTER_ACCESS_USE = 0x00000008;
    public const uint PRINTER_ACCESS_ADMINISTER = 0x00000004;
    public const uint STANDARD_RIGHTS_REQUIRED = 0x000F0000;
    public const uint PRINTER_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | PRINTER_ACCESS_USE | PRINTER_ACCESS_ADMINISTER;

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

    [DllImport(WinspoolDll, SetLastError = true)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool GetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint cbBuf, out uint pcbNeeded);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint command);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindFirstPrinterChangeNotification(IntPtr hPrinter, uint fdwFlags, uint fdwOptions, IntPtr pPrinterNotifyOptions);

    [DllImport(WinspoolDll, SetLastError = true)]
    public static extern bool FindNextPrinterChangeNotification(IntPtr hChange, out uint pdwChange, IntPtr pPrinterNotifyOptions, out IntPtr ppPrinterNotifyInfo);

    [DllImport(WinspoolDll, SetLastError = true)]
    public static extern bool FindClosePrinterChangeNotification(IntPtr hChange);

    [DllImport(WinspoolDll, SetLastError = true)]
    public static extern bool FreePrinterNotifyInfo(IntPtr pPrinterNotifyInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumPrinters(uint Flags, string? Name, uint Level, IntPtr pPrinterEnum, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumJobs(IntPtr hPrinter, uint FirstJob, uint NoJobs, uint Level, IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [DllImport(WinspoolDll, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetPrinter(IntPtr hPrinter, uint Level, IntPtr pPrinter, uint Command);

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

    public const uint PRINTER_CONTROL_PAUSE = 1;
    public const uint PRINTER_CONTROL_RESUME = 2;

    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;

    public static void ThrowIfWin32False(bool result)
    {
        if (!result)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }
}


