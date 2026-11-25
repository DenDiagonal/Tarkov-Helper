using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using SysDiag = System.Diagnostics;

namespace TarkovHelper.Services;

/// <summary>
/// Global keyboard hook service that captures keyboard events even when the app is not in focus.
/// Used to capture NumPad keys for floor selection when EscapeFromTarkov.exe is the foreground window.
/// </summary>
public class GlobalKeyboardHookService : IDisposable
{
    #region Win32 API

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    #endregion

    public static GlobalKeyboardHookService Instance { get; } = new();

    private IntPtr _hookId = IntPtr.Zero;
    // Keep delegate reference to prevent GC collection
    private readonly LowLevelKeyboardProc _proc;
    private System.Windows.Threading.Dispatcher? _hookDispatcher;
    private bool _isHooked;
    private bool _isEnabled;

    [System.Diagnostics.Conditional("DEBUG")]
    private static void Log(string message)
    {
    }

    /// <summary>
    /// Event fired when a NumPad key is pressed while EscapeFromTarkov.exe is the foreground window.
    /// Parameter is the floor index (0-5).
    /// </summary>
    public event Action<int>? FloorKeyPressed;

    #region Overlay MiniMap Hotkey Events

    /// <summary>
    /// 오버레이 토글 (Ctrl+1)
    /// </summary>
    public event Action? OverlayTogglePressed;

    /// <summary>
    /// 오버레이 설정창 열기 (Ctrl+7)
    /// </summary>
    public event Action? OverlaySettingsPressed;

    /// <summary>
    /// 오버레이 클릭 통과 모드 토글 (Ctrl+9)
    /// </summary>
    public event Action? OverlayClickThroughPressed;

    /// <summary>
    /// 오버레이 줌 인 (NumPad +)
    /// </summary>
    public event Action? OverlayZoomInPressed;

    /// <summary>
    /// 오버레이 줌 아웃 (NumPad -)
    /// </summary>
    public event Action? OverlayZoomOutPressed;

    /// <summary>
    /// 설정된 줌 인 단축키 (Virtual Key Code)
    /// </summary>
    public int ZoomInKey { get; set; } = 0x6B; // VK_ADD

    /// <summary>
    /// 설정된 줌 아웃 단축키 (Virtual Key Code)
    /// </summary>
    public int ZoomOutKey { get; set; } = 0x6D; // VK_SUBTRACT

    #endregion

    /// <summary>
    /// Whether the global hook is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;

            if (_isEnabled)
                StartHook();
            else
                StopHook();
        }
    }

    private GlobalKeyboardHookService()
    {
        // Initialize delegate in constructor to prevent GC collection
        _proc = HookCallback;
    }

    private void StartHook()
    {
        if (_isHooked) return;

        // Use null for hMod with WH_KEYBOARD_LL - this is the correct approach for low-level hooks
        _hookDispatcher = System.Windows.Application.Current?.Dispatcher;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
        _isHooked = _hookId != IntPtr.Zero;

        Log($"StartHook: hookId={_hookId}, isHooked={_isHooked}");
        if (!_isHooked)
        {
            var error = Marshal.GetLastWin32Error();
            Log($"SetWindowsHookEx failed with error: {error}");
        }
    }

    private void StopHook()
    {
        if (!_isHooked || _hookId == IntPtr.Zero) return;

        var hookToRemove = _hookId;
        _hookId = IntPtr.Zero;
        _isHooked = false;

        var dispatcher = _hookDispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => UnhookWindowsHookEx(hookToRemove));
        }
        else
        {
            UnhookWindowsHookEx(hookToRemove);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            // Fast candidate filter: if this key is not a candidate key for TarkovHelper, pass it immediately without overhead
            if (!IsCandidateKey(vkCode))
            {
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            // Check if EscapeFromTarkov.exe or TarkovHelper is the foreground window
            if (IsTarkovOrHelperForeground())
            {
                // NumPad keys for floor selection (0-5) - only without Ctrl
                if (!IsCtrlPressed())
                {
                    int? floorIndex = GetFloorIndexFromVkCode(vkCode);
                    if (floorIndex.HasValue)
                    {
                        Log($"NumPad{floorIndex.Value} pressed, firing event");
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            FloorKeyPressed?.Invoke(floorIndex.Value);
                        });
                    }
                }

                // NumPad +/- for overlay zoom (오버레이 활성화 시에만)
                if (IsOverlayVisible)
                {
                    var zoomAction = GetZoomActionFromVkCode(vkCode);
                    if (zoomAction != null)
                    {
                        Log($"NumPad zoom key pressed (overlay visible)");
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            zoomAction.Invoke();
                        });
                    }
                }
            }

            // Global numeric overlay shortcuts (Ctrl+1, Ctrl+7, Ctrl+9)
            // Active ONLY when EFT/TarkovHelper is foreground or the overlay itself is visible (never hijack outside apps)
            if (IsCtrlPressed() && !IsAltPressed() && !IsShiftPressed() && (IsTarkovOrHelperForeground() || IsOverlayVisible))
            {
                const int VK_1 = 0x31;
                const int VK_NUMPAD1 = 0x61;
                const int VK_7 = 0x37;
                const int VK_NUMPAD7 = 0x67;
                const int VK_9 = 0x39;
                const int VK_NUMPAD9 = 0x69;

                // Overlay Toggle Hotkey: Ctrl+1 (toggle overlay open/close regardless of current visibility)
                if (vkCode == VK_1 || vkCode == VK_NUMPAD1)
                {
                    Log("Ctrl+1 pressed: Toggle overlay");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        OverlayTogglePressed?.Invoke();
                    });
                    return (IntPtr)1; // Consume key to prevent duplicate triggering in focused window
                }
                // Overlay Settings Hotkey: Ctrl+7
                else if (vkCode == VK_7 || vkCode == VK_NUMPAD7)
                {
                    Log("Ctrl+7 pressed: Open overlay settings");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        OverlaySettingsPressed?.Invoke();
                    });
                    return (IntPtr)1; // Consume key to prevent duplicate triggering in focused window
                }
                // Overlay Click-Through Hotkey: Ctrl+9
                else if (vkCode == VK_9 || vkCode == VK_NUMPAD9)
                {
                    Log("Ctrl+9 pressed: Toggle click-through");
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        OverlayClickThroughPressed?.Invoke();
                    });
                    return (IntPtr)1; // Consume key to prevent duplicate triggering in focused window
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Fast check if virtual key code is a candidate key for any TarkovHelper action.
    /// </summary>
    private bool IsCandidateKey(int vkCode)
    {
        // Ctrl+1 (0x31/0x61), Ctrl+7 (0x37/0x67), Ctrl+9 (0x39/0x69)
        if (vkCode is 0x31 or 0x61 or 0x37 or 0x67 or 0x39 or 0x69)
            return true;
        // NumPad 0-5 (0x60-0x65)
        if (vkCode is >= 0x60 and <= 0x65)
            return true;
        // Configured zoom keys
        if (vkCode == ZoomInKey || vkCode == ZoomOutKey)
            return true;

        return false;
    }

    /// <summary>
    /// Ctrl 키가 눌려있는지 확인
    /// </summary>
    private static bool IsCtrlPressed()
    {
        return (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12;
    private const int VK_SHIFT = 0x10;

    /// <summary>
    /// Alt 키가 눌려있는지 확인
    /// </summary>
    private static bool IsAltPressed()
    {
        return (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
    }

    /// <summary>
    /// Shift 키가 눌려있는지 확인
    /// </summary>
    private static bool IsShiftPressed()
    {
        return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
    }

    /// <summary>
    /// 오버레이가 현재 표시 중인지 확인
    /// </summary>
    private static bool IsOverlayVisible => OverlayMiniMapService.Instance.IsOverlayVisible;


    /// <summary>
    /// Virtual key code를 줌 액션으로 변환 (설정된 단축키 기반)
    /// </summary>
    private Action? GetZoomActionFromVkCode(int vkCode)
    {
        if (vkCode == ZoomInKey) return OverlayZoomInPressed;
        if (vkCode == ZoomOutKey) return OverlayZoomOutPressed;
        
        return null;
    }

    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;
    private static IntPtr _lastForegroundHwnd = IntPtr.Zero;
    private static uint _lastForegroundPid;
    private static bool _lastForegroundResult;
    private static long _lastForegroundTick64;

    /// <summary>
    /// Checks if EscapeFromTarkov.exe or TarkovHelper is the foreground window.
    /// Uses HWND caching for 500ms to avoid querying process information repeatedly.
    /// Fast Win32 query without allocating System.Diagnostics.Process objects.
    /// </summary>
    private static bool IsTarkovOrHelperForeground()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            long nowTick = Environment.TickCount64;
            if (hwnd == _lastForegroundHwnd && (nowTick - _lastForegroundTick64) < 500)
                return _lastForegroundResult;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            if (pid == CurrentProcessId)
            {
                CacheForeground(hwnd, pid, true, nowTick);
                return true;
            }

            if (pid == _lastForegroundPid && (nowTick - _lastForegroundTick64) < 2000)
                return _lastForegroundResult;

            bool isMatch = false;
            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    var sb = new System.Text.StringBuilder(260);
                    uint capacity = (uint)sb.Capacity;
                    if (QueryFullProcessImageNameW(hProc, 0, sb, ref capacity))
                    {
                        ReadOnlySpan<char> path = sb.ToString().AsSpan(0, (int)capacity);
                        int lastSlash = path.LastIndexOfAny('\\', '/');
                        ReadOnlySpan<char> exe = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;

                        isMatch = exe.Contains("EscapeFromTarkov", StringComparison.OrdinalIgnoreCase) ||
                                  exe.Contains("TarkovHelper", StringComparison.OrdinalIgnoreCase);
                    }
                }
                finally
                {
                    CloseHandle(hProc);
                }
            }

            CacheForeground(hwnd, pid, isMatch, nowTick);
            return isMatch;
        }
        catch
        {
            return false;
        }
    }

    private static void CacheForeground(IntPtr hwnd, uint pid, bool result, long tick)
    {
        _lastForegroundHwnd = hwnd;
        _lastForegroundPid = pid;
        _lastForegroundResult = result;
        _lastForegroundTick64 = tick;
    }

    /// <summary>
    /// Gets the floor index from a virtual key code.
    /// NumPad0 = 0, NumPad1 = 1, ..., NumPad5 = 5
    /// </summary>
    private static int? GetFloorIndexFromVkCode(int vkCode)
    {
        // Virtual key codes for NumPad keys
        const int VK_NUMPAD0 = 0x60;
        const int VK_NUMPAD1 = 0x61;
        const int VK_NUMPAD2 = 0x62;
        const int VK_NUMPAD3 = 0x63;
        const int VK_NUMPAD4 = 0x64;
        const int VK_NUMPAD5 = 0x65;

        return vkCode switch
        {
            VK_NUMPAD0 => 0,
            VK_NUMPAD1 => 1,
            VK_NUMPAD2 => 2,
            VK_NUMPAD3 => 3,
            VK_NUMPAD4 => 4,
            VK_NUMPAD5 => 5,
            _ => null
        };
    }

    public void Dispose()
    {
        StopHook();
        GC.SuppressFinalize(this);
    }

    ~GlobalKeyboardHookService()
    {
        StopHook();
    }
}
