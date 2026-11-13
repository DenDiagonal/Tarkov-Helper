using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// Ensures only a single instance of Tarkov Helper is running per user session.
/// If a secondary instance is launched, it signals the active instance to restore
/// its window from the system tray / bring it to the foreground, then terminates.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private static readonly ILogger _log = Log.For<SingleInstanceService>();
    private const string MutexName = "Local\\TarkovHelper_SingleInstance_Mutex_7A28BF";
    private const string EventName = "Local\\TarkovHelper_BringToFront_Event_7A28BF";

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private static readonly Lazy<SingleInstanceService> _lazy = new(() => new SingleInstanceService());
    public static SingleInstanceService Instance => _lazy.Value;

    private Mutex? _mutex;
    private EventWaitHandle? _eventWaitHandle;
    private CancellationTokenSource? _cts;
    private bool _hasMutexOwnership;
    private bool _disposed;

    private SingleInstanceService() { }

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// Returns true if this is the primary instance.
    /// Returns false if another instance is already running (signaling that instance to focus).
    /// </summary>
    public bool Start()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out _hasMutexOwnership);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed or exited without releasing; this instance now owns the mutex.
            _hasMutexOwnership = true;
        }
        catch (Exception)
        {
            // Fallback: if mutex creation fails for unexpected OS security reasons, allow process to proceed.
            return true;
        }

        if (!_hasMutexOwnership)
        {
            // Another instance is already running. Signal it to restore its window.
            SignalRunningInstance();
            return false;
        }

        // Primary instance: create named event to listen for subsequent launch attempts
        try
        {
            _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            StartListeningForActivation();
        }
        catch (Exception ex)
        {
            _log.Warning($"Could not initialize single instance event listener: {ex.Message}");
        }

        return true;
    }

    private void SignalRunningInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
            {
                using (existingEvent)
                {
                    existingEvent.Set();
                }
            }
        }
        catch
        {
            // Best effort signal
        }
    }

    private void StartListeningForActivation()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() =>
        {
            while (!token.IsCancellationRequested && !_disposed && _eventWaitHandle != null)
            {
                try
                {
                    // Wait with a 1-second timeout to allow responsive shutdown
                    if (_eventWaitHandle.WaitOne(1000))
                    {
                        if (token.IsCancellationRequested || _disposed)
                            break;

                        // Dispatch restore and bring to front on UI thread
                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                var mainWindow = MainWindow.Instance ?? System.Windows.Application.Current?.MainWindow as MainWindow;
                                if (mainWindow != null)
                                {
                                    mainWindow.RestoreFromTray();

                                    var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
                                    if (hwnd != IntPtr.Zero)
                                    {
                                        ShowWindow(hwnd, SW_RESTORE);
                                        SetForegroundWindow(hwnd);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.Error("Error bringing window to front from single-instance signal", ex);
                            }
                        });
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (AbandonedMutexException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Continue listening
                }
            }
        }, token);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch { }

        try
        {
            _eventWaitHandle?.Dispose();
        }
        catch { }

        try
        {
            if (_hasMutexOwnership && _mutex != null)
            {
                _mutex.ReleaseMutex();
            }
            _mutex?.Dispose();
        }
        catch { }
    }
}
