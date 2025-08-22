using System.Runtime.InteropServices;
using PausePrint.Interop;
using System.Management;
using Serilog;

namespace PausePrint.UI.Services;

public sealed class PrintMonitorController : IAsyncDisposable
{
	private CancellationTokenSource? _cts;
	private readonly List<Task> _monitorTasks = new();
	private Task? _pollTask;
	private ManagementEventWatcher? _wmiWatcher;
	private readonly Dictionary<string, HashSet<uint>> _seenJobsByPrinter = new(StringComparer.OrdinalIgnoreCase);
	private readonly object _sync = new();

	private bool _autoHold = true;
	private readonly HashSet<string> _enabledPrinters = new(StringComparer.OrdinalIgnoreCase);

	private int _throttleEveryKJobs;
	private int _throttlePauseSeconds;
	private int _throttleCounter;
	private DateTime _throttleNotBeforeUtc = DateTime.MinValue;
	private readonly SemaphoreSlim _resumeLock = new(1, 1);

	private int _pageEveryN;
	private int _pagePauseSeconds;
	private bool _autoPageThrottle;

	public void SetPageThrottle(int everyN, int pauseSeconds, bool enabled)
	{
		_pageEveryN = Math.Max(0, everyN);
		_pagePauseSeconds = Math.Max(0, pauseSeconds);
		_autoPageThrottle = enabled && _pageEveryN > 0 && _pagePauseSeconds > 0;
	}

	public bool AutoHold
	{
		get => _autoHold;
		set => _autoHold = value;
	}

	public event Action<PrintJobDto>? JobReceived;

	public bool IsRunning => _cts is { IsCancellationRequested: false };
	private string[] _activePrinters = Array.Empty<string>();

	public Task StartAsync()
	{
		if (IsRunning) return Task.CompletedTask;
		_cts = new CancellationTokenSource();
		var token = _cts.Token;

		Log.Information("Starting monitor. AutoHold={AutoHold} EnabledPrinters={Printers}", _autoHold, string.Join(", ", _enabledPrinters));

		_activePrinters = _enabledPrinters.Count > 0 ? _enabledPrinters.ToArray() : EnumeratePrinters().ToArray();
		foreach (var printer in _activePrinters)
		{
			_monitorTasks.Add(Task.Run(() => MonitorPrinterAsync(printer, token), token));
		}
		// Fallback polling to avoid missing notifications
		_pollTask = Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					foreach (var p in _activePrinters)
					{
						PublishNewJobs(p);
					}
				}
				catch (Exception ex) { Log.Error(ex, "Poll loop error"); }
				await Task.Delay(1500, token).ConfigureAwait(false);
			}
		}, token);

		// WMI fallback: listen to print job creations
		try
		{
			var query = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_PrintJob'");
			_wmiWatcher = new ManagementEventWatcher(new ManagementScope("\\\\.\\root\\CIMV2"), query);
			_wmiWatcher.EventArrived += (_, args) =>
			{
				try
				{
					var job = (ManagementBaseObject)args.NewEvent["TargetInstance"];
					var name = (string)(job["Name"] ?? string.Empty); // "PrinterName,JobId"
					var parts = name.Split(',');
					if (parts.Length == 2)
					{
						var printer = parts[0].Trim();
						var idPart = parts[1].Trim();
						Log.Information("WMI job created: {Printer} Raw={Name}", printer, name);
						if (uint.TryParse(idPart, out var jobId))
						{
							if (_enabledPrinters.Count == 0 || _enabledPrinters.Contains(printer))
							{
								// Try pausing immediately using job id
								var user = (job["Owner"] as string) ?? string.Empty;
								var doc = (job["Document"] as string) ?? string.Empty;
								if (_autoHold)
								{
									TryControlJob(printer, jobId, WinSpool.JOB_CONTROL_PAUSE);
									Log.Information("AutoHold paused job {JobId} on {Printer} via WMI", jobId, printer);
								}
								lock (_sync)
								{
									if (!_seenJobsByPrinter.TryGetValue(printer, out var set)) { set = new HashSet<uint>(); _seenJobsByPrinter[printer] = set; }
									if (!set.Contains(jobId))
									{
										set.Add(jobId);
										JobReceived?.Invoke(new PrintJobDto(printer, jobId, user, doc));
									}
								}
								// Also attempt to fetch detailed info from spooler (will no-op if not available)
								PublishSpecificJob(printer, jobId);
							}
						}
					}
				}
				catch (Exception ex) { Log.Error(ex, "WMI EventArrived error"); }
			};
			_wmiWatcher.Start();
		}
		catch (Exception ex) { Log.Error(ex, "WMI watcher start failed"); }
		return Task.CompletedTask;
	}

	public async Task StopAsync()
	{
		if (_cts == null) return;
		_cts.Cancel();
		Log.Information("Stopping monitor");
		try { await Task.WhenAll(_monitorTasks.ToArray()); } catch { }
		if (_pollTask != null) { try { await _pollTask; } catch { } _pollTask = null; }
		try { _wmiWatcher?.Stop(); _wmiWatcher?.Dispose(); _wmiWatcher = null; } catch { }
		_monitorTasks.Clear();
		_cts.Dispose();
		_cts = null;
		_activePrinters = Array.Empty<string>();
	}

	public static IReadOnlyList<string> EnumeratePrinters()
	{
		// Managed fallback for reliability
		try
		{
			var list = new List<string>();
			foreach (string name in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
			{
				if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
			}
			if (list.Count > 0) return list;
		}
		catch { }

		// P/Invoke path
		var flags = WinSpool.PRINTER_ENUM_LOCAL | WinSpool.PRINTER_ENUM_CONNECTIONS;
		WinSpool.EnumPrinters(flags, null, 1, IntPtr.Zero, 0, out var needed, out var returned);
		if (needed == 0)
		{
			return Array.Empty<string>();
		}
		var buffer = Marshal.AllocHGlobal((int)needed);
		try
		{
			if (!WinSpool.EnumPrinters(flags, null, 1, buffer, needed, out needed, out returned) || returned == 0)
			{
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

	public void SetEnabledPrinters(IEnumerable<string> names)
	{
		_enabledPrinters.Clear();
		foreach (var n in names)
		{
			if (!string.IsNullOrWhiteSpace(n)) _enabledPrinters.Add(n);
		}
	}

	public void SetThrottle(int everyKJobs, int pauseSeconds)
	{
		_throttleEveryKJobs = Math.Max(0, everyKJobs);
		_throttlePauseSeconds = Math.Max(0, pauseSeconds);
		_throttleCounter = 0;
		_throttleNotBeforeUtc = DateTime.MinValue;
	}

	private void MonitorPrinterAsync(string printerName, CancellationToken token)
	{
		var defaults = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS, pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero };
		if (!WinSpool.OpenPrinter(printerName, out var hPrinter, ref defaults))
		{
			Log.Warning("OpenPrinter failed for {Printer}. Err={Err}", printerName, Marshal.GetLastWin32Error());
			return;
		}
		try
		{
			var mask = WinSpool.PRINTER_CHANGE_ADD_JOB | WinSpool.PRINTER_CHANGE_SET_JOB | WinSpool.PRINTER_CHANGE_WRITE_JOB;
			var change = WinSpool.FindFirstPrinterChangeNotification(hPrinter, mask, 0, IntPtr.Zero);
			if (change == IntPtr.Zero)
			{
				Log.Warning("FindFirstPrinterChangeNotification failed for {Printer}. Err={Err}", printerName, Marshal.GetLastWin32Error());
				return;
			}
			try
			{
				while (!token.IsCancellationRequested)
				{
					var wait = WinSpool.WaitForSingleObject(change, 5000);
					if (wait == WinSpool.WAIT_TIMEOUT) continue;
					if (!WinSpool.FindNextPrinterChangeNotification(change, out var _, IntPtr.Zero, out var pInfo))
					{
						Log.Warning("FindNextPrinterChangeNotification failed. Err={Err}", Marshal.GetLastWin32Error());
						continue;
					}
					try
					{
						PublishNewJobs(printerName);
					}
					finally
					{
						if (pInfo != IntPtr.Zero)
						{
							WinSpool.FreePrinterNotifyInfo(pInfo);
						}
					}
				}
			}
			finally
			{
				WinSpool.FindClosePrinterChangeNotification(change);
			}
		}
		finally
		{
			WinSpool.ClosePrinter(hPrinter);
		}
	}

	private void PublishNewJobs(string printerName)
	{
		var defaults = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS, pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero };
		if (!WinSpool.OpenPrinter(printerName, out var hPrinter, ref defaults)) { Log.Warning("OpenPrinter (Publish) failed {Printer} Err={Err}", printerName, Marshal.GetLastWin32Error()); return; }
		try
		{
			if (_autoHold)
			{
				// Pause entire queue proactively to prevent immediate output
				WinSpool.SetPrinter(hPrinter, 0, IntPtr.Zero, WinSpool.PRINTER_CONTROL_PAUSE);
			}
			WinSpool.EnumJobs(hPrinter, 0, 100, 1, IntPtr.Zero, 0, out var needed, out var returned);
			if (needed == 0 || returned == 0) { Log.Debug("EnumJobs empty for {Printer}", printerName); return; }
			var buffer = Marshal.AllocHGlobal((int)needed);
			try
			{
				if (!WinSpool.EnumJobs(hPrinter, 0, 100, 1, buffer, needed, out _, out returned) || returned == 0) { Log.Debug("EnumJobs empty for {Printer}", printerName); return; }
				var size = Marshal.SizeOf<JOB_INFO_1>();
				var ptr = buffer;
				lock (_sync)
				{
					if (!_seenJobsByPrinter.TryGetValue(printerName, out var set))
					{
						set = new HashSet<uint>();
						_seenJobsByPrinter[printerName] = set;
					}
					for (var i = 0; i < returned; i++)
					{
						var ji = Marshal.PtrToStructure<JOB_INFO_1>(ptr);
						ptr += size;
						if (!set.Contains(ji.JobId))
						{
							set.Add(ji.JobId);
							var user = InteropHelpers.PtrToString(ji.pUserName);
							var doc = InteropHelpers.PtrToString(ji.pDocument);
							if (_autoHold)
							{
								if (!TryControlJob(printerName, ji.JobId, WinSpool.JOB_CONTROL_PAUSE))
								{
									Log.Warning("AutoHold failed to pause job {JobId} on {Printer} err={Err}", ji.JobId, printerName, Marshal.GetLastWin32Error());
								}
								Log.Information("AutoHold paused job {JobId} on {Printer}", ji.JobId, printerName);
							}
							Log.Information("Publish job {JobId} {User} {Doc} on {Printer}", ji.JobId, user, doc, printerName);
							JobReceived?.Invoke(new PrintJobDto(printerName, ji.JobId, user, doc));
						}
					}
				}
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

	private void PublishSpecificJob(string printerName, uint jobId)
	{
		var defaults = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS, pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero };
		if (!WinSpool.OpenPrinter(printerName, out var hPrinter, ref defaults)) { Log.Warning("OpenPrinter (Specific) failed {Printer} Err={Err}", printerName, Marshal.GetLastWin32Error()); return; }
		try
		{
			WinSpool.GetJob(hPrinter, jobId, 1, IntPtr.Zero, 0, out var needed);
			if (needed == 0) { Log.Debug("GetJob no data for {Printer} job {JobId}", printerName, jobId); return; }
			var buffer = Marshal.AllocHGlobal((int)needed);
			try
			{
				if (!WinSpool.GetJob(hPrinter, jobId, 1, buffer, needed, out _)) { Log.Debug("GetJob failed {Printer} job {JobId} err={Err}", printerName, jobId, Marshal.GetLastWin32Error()); return; }
				var ji = Marshal.PtrToStructure<JOB_INFO_1>(buffer);
				lock (_sync)
				{
					if (!_seenJobsByPrinter.TryGetValue(printerName, out var set)) { set = new HashSet<uint>(); _seenJobsByPrinter[printerName] = set; }
					if (!set.Contains(ji.JobId))
					{
						set.Add(ji.JobId);
						var user = InteropHelpers.PtrToString(ji.pUserName);
						var doc = InteropHelpers.PtrToString(ji.pDocument);
						if (_autoHold)
						{
							TryControlJob(printerName, ji.JobId, WinSpool.JOB_CONTROL_PAUSE);
							Log.Information("AutoHold paused job {JobId} on {Printer}", ji.JobId, printerName);
						}
						Log.Information("Publish specific job {JobId} {User} {Doc} on {Printer}", ji.JobId, user, doc, printerName);
						JobReceived?.Invoke(new PrintJobDto(printerName, ji.JobId, user, doc));
					}
				}
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

	public async Task<bool> ResumeJobAsync(string printerName, uint jobId)
	{
		await _resumeLock.WaitAsync().ConfigureAwait(false);
		try
		{
			// Ensure printer queue is not paused
			var defaultsQ = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS };
			if (WinSpool.OpenPrinter(printerName, out var hq, ref defaultsQ))
			{
				try { WinSpool.SetPrinter(hq, 0, IntPtr.Zero, WinSpool.PRINTER_CONTROL_RESUME); }
				finally { WinSpool.ClosePrinter(hq); }
			}

			if (_throttleEveryKJobs > 0 && _throttlePauseSeconds > 0)
			{
				var now = DateTime.UtcNow;
				if (now < _throttleNotBeforeUtc)
				{
					var delay = _throttleNotBeforeUtc - now;
					await Task.Delay(delay).ConfigureAwait(false);
				}
			}
			var ok = TryControlJob(printerName, jobId, WinSpool.JOB_CONTROL_RESUME);
			if (!ok) { Log.Warning("ResumeJob failed for {Printer} {JobId} err={Err}", printerName, jobId, Marshal.GetLastWin32Error()); }
			if (ok && _autoPageThrottle)
			{
				_ = Task.Run(() => MonitorPagesAndThrottleAsync(printerName, jobId, _pageEveryN, _pagePauseSeconds, _cts?.Token ?? CancellationToken.None));
			}
			if (ok && _throttleEveryKJobs > 0 && _throttlePauseSeconds > 0)
			{
				_throttleCounter++;
				if (_throttleCounter >= _throttleEveryKJobs)
				{
					_throttleCounter = 0;
					_throttleNotBeforeUtc = DateTime.UtcNow.AddSeconds(_throttlePauseSeconds);
				}
			}
			return ok;
		}
		finally
		{
			_resumeLock.Release();
		}
	}

	private void MonitorPagesAndThrottleAsync(string printerName, uint jobId, int pagesEvery, int pauseSeconds, CancellationToken token)
	{
		try
		{
			int lastPages = -1;
			bool waiting = false;
			while (!token.IsCancellationRequested)
			{
				if (!TryGetJobInfo1(printerName, jobId, out var ji)) break;
				var pages = unchecked((int)ji.PagesPrinted);
				if (pages != lastPages)
				{
					lastPages = pages;
					if (pagesEvery > 0 && pages > 0 && (pages % pagesEvery) == 0 && !waiting)
					{
						waiting = true;
						// Pause queue and job to enforce delay even for buffered drivers
						var d = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS };
						if (WinSpool.OpenPrinter(printerName, out var hq, ref d))
						{
							try { WinSpool.SetPrinter(hq, 0, IntPtr.Zero, WinSpool.PRINTER_CONTROL_PAUSE); } finally { WinSpool.ClosePrinter(hq); }
						}
						TryControlJob(printerName, jobId, WinSpool.JOB_CONTROL_PAUSE);
						Log.Information("Page throttle: paused at {Pages} on {Printer} job {JobId}", pages, printerName, jobId);
						Task.Delay(TimeSpan.FromSeconds(pauseSeconds), token).ContinueWith(_ =>
						{
							// Resume queue and job
							var d2 = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS };
							if (WinSpool.OpenPrinter(printerName, out var hqr, ref d2))
							{
								try { WinSpool.SetPrinter(hqr, 0, IntPtr.Zero, WinSpool.PRINTER_CONTROL_RESUME); } finally { WinSpool.ClosePrinter(hqr); }
							}
							TryControlJob(printerName, jobId, WinSpool.JOB_CONTROL_RESUME);
							Log.Information("Page throttle: resumed after {Sec}s on {Printer} job {JobId}", pauseSeconds, printerName, jobId);
							waiting = false;
						}, token);
					}
				}
				Thread.Sleep(300);
			}
		}
		catch
		{
			// ignore
		}
	}

	private bool TryGetJobInfo1(string printerName, uint jobId, out JOB_INFO_1 info)
	{
		info = default;
		var defaults = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS, pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero };
		if (!WinSpool.OpenPrinter(printerName, out var hPrinter, ref defaults)) return false;
		try
		{
			WinSpool.GetJob(hPrinter, jobId, 1, IntPtr.Zero, 0, out var needed);
			if (needed == 0) return false;
			var buffer = Marshal.AllocHGlobal((int)needed);
			try
			{
				if (!WinSpool.GetJob(hPrinter, jobId, 1, buffer, needed, out _)) return false;
				info = Marshal.PtrToStructure<JOB_INFO_1>(buffer);
				return true;
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
	public bool CancelJob(string printerName, uint jobId) => TryControlJob(printerName, jobId, WinSpool.JOB_CONTROL_CANCEL);

	private bool TryControlJob(string printerName, uint jobId, uint control)
	{
		var defaults = new WinSpool.PRINTER_DEFAULTS { DesiredAccess = WinSpool.PRINTER_ALL_ACCESS, pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero };
		if (!WinSpool.OpenPrinter(printerName, out var hPrinter, ref defaults)) { Log.Warning("OpenPrinter (control) failed {Printer} Err={Err}", printerName, Marshal.GetLastWin32Error()); return false; }
		try
		{
			return WinSpool.SetJob(hPrinter, jobId, 0, IntPtr.Zero, control);
		}
		finally
		{
			WinSpool.ClosePrinter(hPrinter);
		}
	}

	public async ValueTask DisposeAsync()
	{
		await StopAsync();
	}
}


