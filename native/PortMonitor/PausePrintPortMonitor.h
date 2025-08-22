#pragma once

#include <windows.h>

// Simple redirecting port monitor that accepts RAW bytes and forwards to a real printer queue.
// Port name syntax: PP:\\<TargetPrinterName>

extern "C" BOOL APIENTRY InitializePrintMonitor2(
    PMONITORINIT pMonitorInit,
    PHANDLE       phMonitor
);

extern "C" BOOL APIENTRY OpenPortEx(
    LPWSTR pPortName,
    DWORD  dwLevel,
    LPBYTE pMonitorParam,
    LPHANDLE phPort,
    HANDLE hMonitor
);

extern "C" BOOL APIENTRY ClosePort(
    HANDLE hPort
);

extern "C" BOOL APIENTRY StartDocPort(
    HANDLE hPort,
    LPWSTR pPrinterName,
    DWORD JobId,
    DWORD Level,
    LPBYTE pDocInfo
);

extern "C" BOOL APIENTRY WritePort(
    HANDLE  hPort,
    LPBYTE  pBuffer,
    DWORD   cbBuf,
    LPDWORD pcbWritten
);

extern "C" BOOL APIENTRY EndDocPort(
    HANDLE hPort
);

extern "C" BOOL APIENTRY EnumPorts(
    LPWSTR pName,
    DWORD  Level,
    LPBYTE pPorts,
    DWORD  cbBuf,
    LPDWORD pcbNeeded,
    LPDWORD pcReturned
);

struct RedirectPort
{
    HANDLE spoolPrinter = nullptr;
    HANDLE spoolJob = nullptr;
    DWORD jobId = 0;
    wchar_t targetPrinter[256]{};
};


