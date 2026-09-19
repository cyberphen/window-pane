using System.Runtime.InteropServices;

namespace WindowPane;

internal sealed class CapturedWindow
{
    public IntPtr Handle { get; init; }
    public IntPtr OriginalParent { get; init; }
    public IntPtr OriginalStyle { get; init; }
    public IntPtr OriginalExStyle { get; init; }
    public NativeMethods.RECT OriginalRect { get; init; }
    public bool WasMaximized { get; init; }
    public string Title { get; set; } = "";
}

internal sealed class WindowHost : IDisposable
{
    private readonly IntPtr _hostHandle;
    private readonly List<CapturedWindow> _captured = new();
    private readonly ITaskbarList? _taskbar;
    private bool _disposed;

    public WindowHost(IntPtr hostHandle)
    {
        _hostHandle = hostHandle;
        try
        {
            _taskbar = (ITaskbarList)new TaskbarList();
            _taskbar.HrInit();
        }
        catch
        {
            _taskbar = null;
        }
    }

    public IReadOnlyList<CapturedWindow> Captured => _captured;

    public event Action? Changed;

    public bool Contains(IntPtr hwnd) => _captured.Exists(c => c.Handle == hwnd);

    public bool TryCapture(IntPtr hwnd, out string? error)
    {
        error = null;
        hwnd = NativeMethods.GetRootWindow(hwnd);

        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            error = "Not a valid window.";
            return false;
        }

        if (hwnd == _hostHandle)
        {
            error = "Cannot capture the pane itself.";
            return false;
        }

        if (Contains(hwnd))
        {
            error = "Window is already inside the pane.";
            return false;
        }

        if (!IsCapturableTopLevel(hwnd, out error))
            return false;

        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        NativeMethods.GetWindowRect(hwnd, out var rect);
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE);
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        var parent = NativeMethods.GetParent(hwnd);
        var title = NativeMethods.GetWindowTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title))
            title = NativeMethods.GetWindowClass(hwnd);

        var captured = new CapturedWindow
        {
            Handle = hwnd,
            OriginalParent = parent,
            OriginalStyle = style,
            OriginalExStyle = exStyle,
            OriginalRect = rect,
            WasMaximized = NativeMethods.IsZoomed(hwnd),
            Title = title
        };

        var newEx = exStyle.ToInt64();
        newEx |= NativeMethods.WS_EX_TOOLWINDOW;
        newEx &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (IntPtr)newEx);

        try { _taskbar?.DeleteTab(hwnd); } catch { /* ignore */ }

        var newStyle = style.ToInt64();
        newStyle |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
        newStyle &= ~(NativeMethods.WS_POPUP | NativeMethods.WS_MINIMIZE | NativeMethods.WS_MAXIMIZE);
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, (IntPtr)newStyle);

        NativeMethods.SetParent(hwnd, _hostHandle);

        if (NativeMethods.GetParent(hwnd) != _hostHandle)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, style);
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            try { _taskbar?.AddTab(hwnd); } catch { /* ignore */ }
            error = "This app refused to be hosted (common with some UWP / elevated windows).";
            return false;
        }

        NativeMethods.SetWindowPos(
            hwnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE |
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED |
            NativeMethods.SWP_SHOWWINDOW);

        _captured.Add(captured);
        Changed?.Invoke();
        return true;
    }

    public bool Release(IntPtr hwnd, bool activate = true)
    {
        var index = _captured.FindIndex(c => c.Handle == hwnd);
        if (index < 0) return false;
        var captured = _captured[index];
        _captured.RemoveAt(index);
        RestoreWindow(captured, activate);
        Changed?.Invoke();
        return true;
    }

    public void ReleaseAll()
    {
        var copy = _captured.ToList();
        _captured.Clear();
        foreach (var captured in copy)
            RestoreWindow(captured, activate: false);
        Changed?.Invoke();
    }

    public void LayoutActive(IntPtr activeHwnd, Rectangle clientBounds)
    {
        foreach (var item in _captured.ToList())
        {
            if (!NativeMethods.IsWindow(item.Handle))
            {
                _captured.Remove(item);
                continue;
            }

            if (item.Handle == activeHwnd)
            {
                NativeMethods.SetWindowPos(
                    item.Handle, IntPtr.Zero,
                    clientBounds.X, clientBounds.Y,
                    Math.Max(clientBounds.Width, 50),
                    Math.Max(clientBounds.Height, 50),
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
                NativeMethods.ShowWindow(item.Handle, NativeMethods.SW_SHOW);
            }
            else
            {
                NativeMethods.ShowWindow(item.Handle, NativeMethods.SW_HIDE);
            }
        }
    }

    public void PruneDeadWindows()
    {
        var removed = _captured.RemoveAll(c => !NativeMethods.IsWindow(c.Handle));
        if (removed > 0) Changed?.Invoke();
    }

    private void RestoreWindow(CapturedWindow captured, bool activate)
    {
        if (!NativeMethods.IsWindow(captured.Handle))
            return;

        NativeMethods.ShowWindow(captured.Handle, NativeMethods.SW_SHOW);

        var restoreParent = captured.OriginalParent;
        if (restoreParent != IntPtr.Zero && !NativeMethods.IsWindow(restoreParent))
            restoreParent = IntPtr.Zero;

        NativeMethods.SetParent(captured.Handle, restoreParent);
        NativeMethods.SetWindowLongPtr(captured.Handle, NativeMethods.GWL_STYLE, captured.OriginalStyle);
        NativeMethods.SetWindowLongPtr(captured.Handle, NativeMethods.GWL_EXSTYLE, captured.OriginalExStyle);

        var r = captured.OriginalRect;
        NativeMethods.SetWindowPos(
            captured.Handle, IntPtr.Zero,
            r.Left, r.Top, r.Width, r.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW);

        if (captured.WasMaximized)
            NativeMethods.ShowWindow(captured.Handle, 3); // SW_MAXIMIZE

        try { _taskbar?.AddTab(captured.Handle); } catch { /* ignore */ }

        if (activate)
        {
            NativeMethods.SetWindowPos(
                captured.Handle, IntPtr.Zero,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    private static bool IsCapturableTopLevel(IntPtr hwnd, out string? error)
    {
        error = null;
        var className = NativeMethods.GetWindowClass(hwnd);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "NotifyIconOverflowWindow")
        {
            error = "System / shell windows cannot be captured.";
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            error = "Could not identify the window process.";
            return false;
        }

        if ((int)pid == Environment.ProcessId)
        {
            error = "Cannot capture Window Pane's own windows.";
            return false;
        }

        if (!TargetPolicy.IsAllowed(hwnd, out error))
            return false;

        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt64();
        if ((style & NativeMethods.WS_CHILD) != 0)
        {
            error = "Only top-level windows can be dropped in.";
            return false;
        }

        NativeMethods.GetWindowRect(hwnd, out var rect);
        if (rect.Width < 80 || rect.Height < 80)
        {
            error = "Window is too small to host.";
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseAll();
    }
}
