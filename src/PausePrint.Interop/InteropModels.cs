using System;
using System.Runtime.InteropServices;

namespace PausePrint.Interop;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct JOB_INFO_1
{
    public uint JobId;
    public IntPtr pPrinterName;
    public IntPtr pMachineName;
    public IntPtr pUserName;
    public IntPtr pDocument;
    public IntPtr pDatatype;
    public IntPtr pStatus;
    public uint Status;
    public uint Priority;
    public uint Position;
    public uint TotalPages;
    public uint PagesPrinted;
    public System.Runtime.InteropServices.ComTypes.FILETIME Submitted;
}

public static class InteropHelpers
{
    public static string PtrToString(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(ptr) ?? string.Empty;
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct PRINTER_INFO_1
{
    public uint Flags;
    public IntPtr pDescription;
    public IntPtr pName;
    public IntPtr pComment;
}


