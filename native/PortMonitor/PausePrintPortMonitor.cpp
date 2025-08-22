#include "PausePrintPortMonitor.h"
#include <winspool.h>
#include <strsafe.h>

static MONITORINIT2 g_init = { 0 };

extern "C" BOOL APIENTRY InitializePrintMonitor2(PMONITORINIT pMonitorInit, PHANDLE phMonitor)
{
    if (!pMonitorInit || !phMonitor) return FALSE;
    g_init.cbSize = sizeof(MONITORINIT2);
    g_init.pMonitorName = pMonitorInit->pMonitorName;
    g_init.hSpooler = pMonitorInit->hSpooler;
    *phMonitor = (HANDLE)1;
    return TRUE;
}

extern "C" BOOL APIENTRY OpenPortEx(LPWSTR pPortName, DWORD /*dwLevel*/, LPBYTE /*pMonitorParam*/, LPHANDLE phPort, HANDLE /*hMonitor*/)
{
    if (!pPortName || !phPort) return FALSE;
    auto port = new RedirectPort();
    // Expected format: PP:\\TargetPrinter
    const wchar_t* prefix = L"PP:\\";
    if (wcsncmp(pPortName, prefix, 4) == 0) {
        StringCchCopyW(port->targetPrinter, 256, pPortName + 4);
    } else {
        // Treat as direct printer name
        StringCchCopyW(port->targetPrinter, 256, pPortName);
    }
    *phPort = port;
    return TRUE;
}

extern "C" BOOL APIENTRY ClosePort(HANDLE hPort)
{
    auto port = reinterpret_cast<RedirectPort*>(hPort);
    if (!port) return FALSE;
    delete port;
    return TRUE;
}

extern "C" BOOL APIENTRY StartDocPort(HANDLE hPort, LPWSTR /*pPrinterName*/, DWORD JobId, DWORD /*Level*/, LPBYTE /*pDocInfo*/)
{
    auto port = reinterpret_cast<RedirectPort*>(hPort);
    if (!port) return FALSE;

    HANDLE hPrinter = nullptr;
    if (!OpenPrinterW(port->targetPrinter, &hPrinter, nullptr)) {
        return FALSE;
    }
    port->spoolPrinter = hPrinter;

    DOC_INFO_1W di = { 0 };
    di.pDocName = const_cast<LPWSTR>(L"PausePrint Redirect");
    di.pDatatype = const_cast<LPWSTR>(L"RAW");
    DWORD job = StartDocPrinterW(hPrinter, 1, (LPBYTE)&di);
    if (job == 0) {
        ClosePrinter(hPrinter);
        port->spoolPrinter = nullptr;
        return FALSE;
    }
    port->jobId = job;
    StartPagePrinter(hPrinter);
    return TRUE;
}

extern "C" BOOL APIENTRY WritePort(HANDLE hPort, LPBYTE pBuffer, DWORD cbBuf, LPDWORD pcbWritten)
{
    auto port = reinterpret_cast<RedirectPort*>(hPort);
    if (!port || !port->spoolPrinter) return FALSE;
    DWORD written = 0;
    BOOL ok = WritePrinter(port->spoolPrinter, pBuffer, cbBuf, &written);
    if (pcbWritten) *pcbWritten = written;
    return ok;
}

extern "C" BOOL APIENTRY EndDocPort(HANDLE hPort)
{
    auto port = reinterpret_cast<RedirectPort*>(hPort);
    if (!port || !port->spoolPrinter) return FALSE;
    EndPagePrinter(port->spoolPrinter);
    EndDocPrinter(port->spoolPrinter);
    ClosePrinter(port->spoolPrinter);
    port->spoolPrinter = nullptr;
    return TRUE;
}

extern "C" BOOL APIENTRY EnumPorts(LPWSTR /*pName*/, DWORD Level, LPBYTE pPorts, DWORD cbBuf, LPDWORD pcbNeeded, LPDWORD pcReturned)
{
    // Minimal: no dynamic ports enumerated; ports are added manually in UI/installer
    *pcbNeeded = 0;
    *pcReturned = 0;
    return TRUE;
}


