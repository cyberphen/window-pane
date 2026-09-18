using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WindowPane;

internal sealed class MainForm : Form
{
    private static readonly int BorderThicknessDefault = 2;
    private const int EdgeThickness = 6;
    private const int TabBarHeight = 36;
    private const int MinDragPixels = 12;
    private const int SideBarWidth = 36;
    private const int RevealZoneWidth = 28;
    /// <summary>Keep clear of Windows Snap / Snap Layouts edge zones.</summary>
    private const int SystemSnapZone = 32;

    private readonly AppSettings _settings;
    private ThemeColors _theme;

    private readonly Panel _sideBar;
    private readonly TrafficDot _closeDot;
    private readonly TrafficDot _minimizeDot;
    private readonly TrafficDot _maximizeDot;
    private readonly FullscreenButton _fullscreenBtn;
    private readonly SettingsButton _settingsBtn;
    private readonly Panel _hostPanel;
    private readonly FlowLayoutPanel _tabBar;
    private readonly Label _hintLabel;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _pruneTimer;
    private readonly System.Windows.Forms.Timer _chromeTimer;
    private readonly ToolTip _tabTip = new();
    private readonly ToolTip _dotTip = new();

    private WindowHost? _host;
    private IntPtr _activeChild = IntPtr.Zero;

    private NativeMethods.LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook = IntPtr.Zero;
    private IntPtr _dragCandidate = IntPtr.Zero;
    private NativeMethods.RECT _dragStartRect;
    private bool _windowDragActive;
    private bool _dropHighlight;

    private bool _isFullscreen;
    private bool _isMaximized;
    private Rectangle _restoreBounds;
    private FormWindowState _restoreState = FormWindowState.Normal;
    private bool _chromeVisible = true;
    private int _pendingResizeHit;

    public MainForm()
    {
        _settings = AppSettings.Load();
        _theme = ThemeCatalog.Get(_settings.Theme);

        Text = "Window Pane";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 320);
        Size = new Size(1100, 720);
        BackColor = _theme.Border;
        Padding = _settings.ShowBorder ? new Padding(BorderThicknessDefault) : Padding.Empty;
        KeyPreview = true;
        DoubleBuffered = true;

        _sideBar = new Panel
        {
            Dock = DockStyle.Left,
            Width = SideBarWidth,
            BackColor = _theme.SideBar
        };

        _closeDot = MakeDot(Color.FromArgb(255, 95, 86), "Close", 12);
        _minimizeDot = MakeDot(Color.FromArgb(255, 189, 46), "Minimize", 36);
        _maximizeDot = MakeDot(Color.FromArgb(39, 201, 63), "Maximize", 60);

        _fullscreenBtn = new FullscreenButton
        {
            Size = new Size(18, 18),
            Cursor = Cursors.Hand
        };
        _dotTip.SetToolTip(_fullscreenBtn, "Full screen");

        _settingsBtn = new SettingsButton
        {
            Size = new Size(18, 18),
            Cursor = Cursors.Hand
        };
        _dotTip.SetToolTip(_settingsBtn, "Settings");

        _closeDot.Click += (_, _) => Close();
        _minimizeDot.Click += (_, _) =>
        {
            if (_isFullscreen) return;
            WindowState = FormWindowState.Minimized;
        };
        _maximizeDot.Click += (_, _) => ToggleMaximize();
        _fullscreenBtn.Click += (_, _) => ToggleFullscreen();
        _settingsBtn.Click += (_, _) => OpenSettings();
        _sideBar.MouseDown += OnSideBarMouseDown;

        _sideBar.Controls.Add(_closeDot);
        _sideBar.Controls.Add(_minimizeDot);
        _sideBar.Controls.Add(_maximizeDot);
        _sideBar.Controls.Add(_fullscreenBtn);
        _sideBar.Controls.Add(_settingsBtn);
        _sideBar.Resize += (_, _) => PositionBottomButtons();
        PositionBottomButtons();

        _hostPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Content,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        _hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = _theme.HintText,
            Font = new Font("Segoe UI", 12f, FontStyle.Regular),
            Text = _settings.GetEmptyHintText(),
            BackColor = Color.Transparent
        };
        _hostPanel.Controls.Add(_hintLabel);

        _tabBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = TabBarHeight,
            BackColor = _theme.TabBar,
            Padding = new Padding(6, 4, 6, 4),
            WrapContents = false,
            AutoScroll = true,
            Visible = false
        };

        Controls.Add(_hostPanel);
        Controls.Add(_tabBar);
        Controls.Add(_sideBar);

        _menu = BuildMenu();
        ContextMenuStrip = _menu;
        _sideBar.ContextMenuStrip = _menu;

        _tray = new NotifyIcon
        {
            Text = "Window Pane",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) =>
        {
            Show();
            if (_isFullscreen)
                ApplyFullscreenBounds();
            else
                WindowState = FormWindowState.Normal;
            Activate();
        };

        _pruneTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _pruneTimer.Tick += (_, _) =>
        {
            _host?.PruneDeadWindows();
            if (_host is not null && _activeChild != IntPtr.Zero && !NativeMethods.IsWindow(_activeChild))
                SelectFirstOrNone();
        };

        _chromeTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _chromeTimer.Tick += (_, _) => UpdateChromeVisibility();

        Load += OnLoaded;
        FormClosing += OnFormClosing;
        Resize += (_, _) =>
        {
            LayoutHosted();
            SyncMaximizeFromSystem();
        };
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Thick frame + caption enable Windows Snap. Visible NC chrome is removed in WM_NCCALCSIZE.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= NativeMethods.WS_THICKFRAME
                        | NativeMethods.WS_CAPTION
                        | NativeMethods.WS_MAXIMIZEBOX
                        | NativeMethods.WS_MINIMIZEBOX;
            return cp;
        }
    }

    private TrafficDot MakeDot(Color color, string tip, int top)
    {
        var dot = new TrafficDot(color)
        {
            Left = (SideBarWidth - 14) / 2,
            Top = top
        };
        _dotTip.SetToolTip(dot, tip);
        return dot;
    }

    private void PositionBottomButtons()
    {
        const int gap = 10;
        const int bottomPad = 12;

        _settingsBtn.Left = (SideBarWidth - _settingsBtn.Width) / 2;
        _settingsBtn.Top = Math.Max(120, _sideBar.ClientSize.Height - _settingsBtn.Height - bottomPad);

        _fullscreenBtn.Left = (SideBarWidth - _fullscreenBtn.Width) / 2;
        _fullscreenBtn.Top = Math.Max(90, _settingsBtn.Top - _fullscreenBtn.Height - gap);
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        _settings.Save();
        ApplySettings();
    }

    private void ApplySettings()
    {
        _theme = ThemeCatalog.Get(_settings.Theme);
        ApplyWindowChrome(_isFullscreen);
        _sideBar.BackColor = _theme.SideBar;
        _tabBar.BackColor = _theme.TabBar;
        SetDropHighlight(_dropHighlight);
        RefreshEmptyHint();
        LayoutHosted();
        Invalidate(true);
    }

    private void RefreshEmptyHint()
    {
        var empty = (_host?.Captured.Count ?? 0) == 0;
        if (!empty)
        {
            _hintLabel.Visible = false;
            return;
        }

        if (_settings.EmptyContent == EmptyContentMode.Blank)
        {
            _hintLabel.Visible = false;
            return;
        }

        _hintLabel.Text = _settings.GetEmptyHintText();
        _hintLabel.Visible = true;
        _hintLabel.ForeColor = _dropHighlight ? _theme.HintTextHighlight : _theme.HintText;
    }

    private void OnSideBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        // Empty bar space moves the pane; dots/buttons handle their own clicks.
        BeginFormDrag();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture foreground window (Ctrl+Shift+A)", null, (_, _) => CaptureForeground());
        menu.Items.Add("Release active window", null, (_, _) => ReleaseActive());
        menu.Items.Add("Release all windows", null, (_, _) =>
        {
            _host?.ReleaseAll();
            _activeChild = IntPtr.Zero;
            RefreshUi();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Full screen", null, (_, _) => ToggleFullscreen());
        var onTop = new ToolStripMenuItem("Always on top") { CheckOnClick = true };
        onTop.CheckedChanged += (_, _) => TopMost = onTop.Checked;
        menu.Items.Add(onTop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close pane (restore windows)", null, (_, _) => Close());
        menu.Items.Add("Exit", null, (_, _) => Close());
        return menu;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _host = new WindowHost(_hostPanel.Handle);
        _host.Changed += () => BeginInvoke(RefreshUi);
        HideSystemBorderLine();
        InstallMouseHook();
        _pruneTimer.Start();
        _chromeTimer.Start();
        ApplySettings();
        RefreshUi();
        SetChromeVisible(true);
    }

    private void HideSystemBorderLine()
    {
        // Win11: remove the light DWM border line while keeping thick-frame Snap.
        var color = NativeMethods.DWMWA_COLOR_NONE;
        _ = NativeMethods.DwmSetWindowAttribute(
            Handle, NativeMethods.DWMWA_BORDER_COLOR, ref color, sizeof(int));
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _pruneTimer.Stop();
        _chromeTimer.Stop();
        UninstallMouseHook();
        _host?.ReleaseAll();
        _host?.Dispose();
        _host = null;
        _tray.Visible = false;
        _tray.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.A)
        {
            CaptureForeground();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_isFullscreen)
                ToggleFullscreen();
            else
                Close();
            e.Handled = true;
        }
    }

    private void BeginFormDrag()
    {
        if (_isFullscreen || _isMaximized) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
    }

    private void BeginEdgeResize(int hitTest)
    {
        if (_isFullscreen || _isMaximized || hitTest == NativeMethods.HTCLIENT) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)hitTest, IntPtr.Zero);
    }

    private void ApplyWindowChrome(bool fullscreen)
    {
        if (fullscreen)
        {
            Padding = Padding.Empty;
            BackColor = _theme.Content;
        }
        else
        {
            Padding = _settings.ShowBorder ? new Padding(BorderThicknessDefault) : Padding.Empty;
            BackColor = _settings.ShowBorder ? _theme.Border : _theme.Content;
        }
    }

    private void ToggleMaximize()
    {
        if (_isFullscreen)
            ToggleFullscreen();

        if (!_isMaximized)
        {
            _restoreBounds = Bounds;
            _isMaximized = true;
            Bounds = Screen.FromControl(this).WorkingArea;
            _dotTip.SetToolTip(_maximizeDot, "Restore");
        }
        else
        {
            _isMaximized = false;
            Bounds = _restoreBounds;
            _dotTip.SetToolTip(_maximizeDot, "Maximize");
        }

        LayoutHosted();
    }

    private void ToggleFullscreen()
    {
        if (!_isFullscreen)
        {
            if (!_isMaximized)
                _restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _restoreState = WindowState;
            _isFullscreen = true;
            _isMaximized = false;
            WindowState = FormWindowState.Normal;
            ApplyWindowChrome(fullscreen: true);
            ApplyFullscreenBounds();
            _fullscreenBtn.IsFullscreen = true;
            _dotTip.SetToolTip(_fullscreenBtn, "Exit full screen");
            _dotTip.SetToolTip(_maximizeDot, "Maximize");
            SetChromeVisible(false);
        }
        else
        {
            _isFullscreen = false;
            ApplyWindowChrome(fullscreen: false);
            Bounds = _restoreBounds;
            WindowState = _restoreState;
            _fullscreenBtn.IsFullscreen = false;
            _dotTip.SetToolTip(_fullscreenBtn, "Full screen");
            SetChromeVisible(true);
        }

        LayoutHosted();
    }

    private void ApplyFullscreenBounds()
    {
        var screen = Screen.FromControl(this);
        Bounds = screen.Bounds;
    }

    private void UpdateChromeVisibility()
    {
        if (!_isFullscreen)
        {
            if (!_chromeVisible)
                SetChromeVisible(true);
            return;
        }

        if (!IsHandleCreated || WindowState == FormWindowState.Minimized)
            return;

        var cursor = Cursor.Position;
        var win = Bounds;
        bool nearLeft = cursor.X >= win.Left - 4
                        && cursor.X <= win.Left + RevealZoneWidth
                        && cursor.Y >= win.Top
                        && cursor.Y <= win.Bottom;

        bool overChrome = _sideBar.Visible
                          && _sideBar.RectangleToScreen(_sideBar.ClientRectangle).Contains(cursor);

        SetChromeVisible(nearLeft || overChrome);
    }

    private void SetChromeVisible(bool visible)
    {
        if (_chromeVisible == visible && _sideBar.Visible == visible) return;
        _chromeVisible = visible;
        _sideBar.Visible = visible;
        LayoutHosted();
    }

    /// <summary>
    /// Hit-test against the outer window rectangle (no visible border/padding).
    /// Works even when child HWNDs cover the client area.
    /// </summary>
    private int HitTestEdges(Point screenPt)
    {
        if (_isFullscreen || _isMaximized) return NativeMethods.HTCLIENT;

        var r = Bounds;
        bool left = screenPt.X >= r.Left && screenPt.X < r.Left + EdgeThickness;
        bool right = screenPt.X < r.Right && screenPt.X >= r.Right - EdgeThickness;
        bool top = screenPt.Y >= r.Top && screenPt.Y < r.Top + EdgeThickness;
        bool bottom = screenPt.Y < r.Bottom && screenPt.Y >= r.Bottom - EdgeThickness;

        if (!r.Contains(screenPt) && !(left || right || top || bottom))
            return NativeMethods.HTCLIENT;

        if (top && left) return NativeMethods.HTTOPLEFT;
        if (top && right) return NativeMethods.HTTOPRIGHT;
        if (bottom && left) return NativeMethods.HTBOTTOMLEFT;
        if (bottom && right) return NativeMethods.HTBOTTOMRIGHT;
        if (left) return NativeMethods.HTLEFT;
        if (right) return NativeMethods.HTRIGHT;
        if (top) return NativeMethods.HTTOP;
        if (bottom) return NativeMethods.HTBOTTOM;
        return NativeMethods.HTCLIENT;
    }

    private static Cursor CursorForHit(int hit) => hit switch
    {
        NativeMethods.HTTOPLEFT or NativeMethods.HTBOTTOMRIGHT => Cursors.SizeNWSE,
        NativeMethods.HTTOPRIGHT or NativeMethods.HTBOTTOMLEFT => Cursors.SizeNESW,
        NativeMethods.HTLEFT or NativeMethods.HTRIGHT => Cursors.SizeWE,
        NativeMethods.HTTOP or NativeMethods.HTBOTTOM => Cursors.SizeNS,
        _ => Cursors.Default
    };

    private void ApplyHoverCursor(Cursor cursor)
    {
        if (Cursor != cursor)
            Cursor = cursor;

        // LL mouse hooks can leave Cursor.Current stuck on the last resize shape.
        if (Cursor.Current != cursor)
            Cursor.Current = cursor;

        // Ensure content controls don't keep an inherited resize cursor.
        if (cursor == Cursors.Default)
        {
            if (_hostPanel.Cursor != Cursors.Default) _hostPanel.Cursor = Cursors.Default;
            if (_hintLabel.Cursor != Cursors.Default) _hintLabel.Cursor = Cursors.Default;
            if (_tabBar.Cursor != Cursors.Default) _tabBar.Cursor = Cursors.Default;
            if (_sideBar.Cursor != Cursors.Default) _sideBar.Cursor = Cursors.Default;
        }
    }

    protected override void WndProc(ref Message m)
    {
        // Cover the whole window with the client area so caption/thick-frame stay
        // invisible, while Windows Snap still sees a snappable frame.
        if (m.Msg == NativeMethods.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        if (m.Msg == NativeMethods.WM_NCHITTEST && !_isFullscreen)
        {
            short sx = unchecked((short)(m.LParam.ToInt64() & 0xFFFF));
            short sy = unchecked((short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            var hit = HitTestEdges(new Point(sx, sy));
            if (hit != NativeMethods.HTCLIENT)
            {
                m.Result = (IntPtr)hit;
                return;
            }
        }

        base.WndProc(ref m);
    }

    private void CaptureForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == Handle) return;
        TryCapture(hwnd);
    }

    private void TryCapture(IntPtr hwnd)
    {
        if (_host is null) return;
        if (!_host.TryCapture(hwnd, out var error))
        {
            if (!string.IsNullOrEmpty(error))
                _tray.ShowBalloonTip(2500, "Window Pane", error, ToolTipIcon.Info);
            return;
        }

        _activeChild = NativeMethods.GetRootWindow(hwnd);
        RefreshUi();
        LayoutHosted();
        Activate();
    }

    private void ReleaseActive()
    {
        if (_host is null || _activeChild == IntPtr.Zero) return;
        var hwnd = _activeChild;
        _host.Release(hwnd);
        SelectFirstOrNone();
        RefreshUi();
        LayoutHosted();
    }

    private void SelectFirstOrNone()
    {
        if (_host is null || _host.Captured.Count == 0)
        {
            _activeChild = IntPtr.Zero;
            return;
        }

        _activeChild = _host.Captured[0].Handle;
    }

    private void RefreshUi()
    {
        if (_host is null) return;

        _host.PruneDeadWindows();
        var items = _host.Captured.ToList();
        _tabBar.Visible = items.Count > 0;
        _tabBar.Controls.Clear();
        RefreshEmptyHint();

        if (_activeChild != IntPtr.Zero && items.TrueForAll(i => i.Handle != _activeChild))
            SelectFirstOrNone();

        foreach (var item in items)
        {
            var btn = new Button
            {
                Text = Truncate(item.Title, 28),
                AutoSize = true,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = item.Handle == _activeChild
                    ? Color.FromArgb(55, 95, 160)
                    : Color.FromArgb(45, 48, 54),
                Margin = new Padding(2, 0, 2, 0),
                Tag = item.Handle,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += (_, _) =>
            {
                _activeChild = (IntPtr)btn.Tag!;
                RefreshUi();
                LayoutHosted();
            };
            btn.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Middle) return;
                _host.Release((IntPtr)btn.Tag!);
                SelectFirstOrNone();
                RefreshUi();
                LayoutHosted();
            };
            _tabTip.SetToolTip(btn, item.Title + "\nMiddle-click to release");
            _tabBar.Controls.Add(btn);
        }

        Text = items.Count == 0
            ? "Window Pane"
            : $"Window Pane ({items.Count})";

        SetDropHighlight(_dropHighlight);
        LayoutHosted();
    }

    private void LayoutHosted()
    {
        if (_host is null || _activeChild == IntPtr.Zero) return;
        _host.LayoutActive(_activeChild, _hostPanel.ClientRectangle);
    }

    private void SetDropHighlight(bool on)
    {
        _dropHighlight = on;
        _hostPanel.BackColor = on ? _theme.DropHighlight : _theme.Content;
        _hintLabel.ForeColor = on ? _theme.HintTextHighlight : _theme.HintText;
        if (on)
        {
            _hintLabel.Text = "Release to dock window here";
            _hintLabel.Visible = (_host?.Captured.Count ?? 0) == 0;
        }
        else
        {
            RefreshEmptyHint();
        }
    }

    private void InstallMouseHook()
    {
        _mouseProc = MouseHookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _mouseProc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);

        if (_mouseHook == IntPtr.Zero)
            _tray.ShowBalloonTip(3000, "Window Pane",
                "Mouse hook failed — use Ctrl+Shift+A to capture the foreground window.",
                ToolTipIcon.Warning);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        _mouseProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            var screenPt = new Point(data.pt.X, data.pt.Y);

            // Edge/corner resize cursor — set on edges, clear elsewhere.
            // Never fight Windows Snap cursors/overlays in screen-edge zones.
            if (!_isFullscreen && !_isMaximized && IsHandleCreated
                && WindowState != FormWindowState.Minimized
                && _dragCandidate == IntPtr.Zero
                && msg == NativeMethods.WM_MOUSEMOVE)
            {
                Cursor cursor = Cursors.Default;
                if (Bounds.Contains(screenPt) && !IsInSystemSnapZone(screenPt))
                {
                    var hit = HitTestEdges(screenPt);
                    if (hit != NativeMethods.HTCLIENT)
                        cursor = CursorForHit(hit);
                }

                try { BeginInvoke(() => ApplyHoverCursor(cursor)); }
                catch (InvalidOperationException) { /* shutting down */ }
            }

            if (!_isFullscreen && !_isMaximized && IsHandleCreated
                && WindowState != FormWindowState.Minimized
                && Bounds.Contains(screenPt)
                && !IsInSystemSnapZone(screenPt)
                && msg == NativeMethods.WM_LBUTTONDOWN)
            {
                var hit = HitTestEdges(screenPt);
                if (hit != NativeMethods.HTCLIENT)
                {
                    _pendingResizeHit = hit;
                    ClearDockDrag();
                    try { BeginInvoke(() => BeginEdgeResize(_pendingResizeHit)); }
                    catch (InvalidOperationException) { /* shutting down */ }
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }
            }

            if (msg == NativeMethods.WM_LBUTTONDOWN)
            {
                if (_pendingResizeHit != 0)
                {
                    _pendingResizeHit = 0;
                }
                else
                {
                    TryBeginDockDrag(screenPt);
                }
            }
            else if (msg == NativeMethods.WM_MOUSEMOVE && _dragCandidate != IntPtr.Zero)
            {
                UpdateDockDragState(screenPt);
            }
            else if (msg == NativeMethods.WM_LBUTTONUP)
            {
                var candidate = _dragCandidate;
                var windowMoved = _windowDragActive;
                ClearDockDrag();
                _pendingResizeHit = 0;

                if (_dropHighlight)
                {
                    try { BeginInvoke(() => SetDropHighlight(false)); }
                    catch (InvalidOperationException) { /* shutting down */ }
                }

                // Only dock when the window itself was moved (title-bar drag), not text/selection drags.
                // Never steal Windows Snap when the cursor is in a screen-edge snap zone.
                if (windowMoved && candidate != IntPtr.Zero && Bounds.Contains(screenPt)
                    && !IsInSystemSnapZone(screenPt)
                    && NativeMethods.IsWindow(candidate)
                    && candidate != Handle)
                {
                    try { BeginInvoke(() => TryCapture(candidate)); }
                    catch (InvalidOperationException) { /* shutting down */ }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ClearDockDrag()
    {
        _dragCandidate = IntPtr.Zero;
        _windowDragActive = false;
        _dragStartRect = default;
    }

    private void TryBeginDockDrag(Point screenPt)
    {
        ClearDockDrag();

        var hitWnd = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = screenPt.X, Y = screenPt.Y });
        var root = NativeMethods.GetRootWindow(hitWnd);
        if (root == IntPtr.Zero || root == Handle || (_host is not null && _host.Contains(root)))
            return;

        var ncHit = NativeMethods.NcHitTest(root, screenPt);
        // Ignore resize grips / buttons — only caption (or custom drag chrome that moves the window).
        if (IsForeignResizeHit(ncHit))
            return;

        // Standard title bar: require caption hit.
        // Custom title bars (Chrome, etc.) often report HTCLIENT — those are accepted only if
        // the window rectangle later moves with unchanged size (see UpdateDockDragState).
        if (ncHit != NativeMethods.HTCAPTION && ncHit != NativeMethods.HTCLIENT)
            return;

        if (!NativeMethods.GetWindowRect(root, out var rect))
            return;

        _dragCandidate = root;
        _dragStartRect = rect;
        _windowDragActive = false;
    }

    private void UpdateDockDragState(Point screenPt)
    {
        if (_dragCandidate == IntPtr.Zero || !NativeMethods.IsWindow(_dragCandidate))
        {
            ClearDockDrag();
            return;
        }

        if (!NativeMethods.GetWindowRect(_dragCandidate, out var now))
            return;

        var sizeSame = Math.Abs(now.Width - _dragStartRect.Width) <= 2
                       && Math.Abs(now.Height - _dragStartRect.Height) <= 2;
        var dx = Math.Abs(now.Left - _dragStartRect.Left);
        var dy = Math.Abs(now.Top - _dragStartRect.Top);
        var positionMoved = dx >= MinDragPixels || dy >= MinDragPixels;

        // Text selection / scrollbar / drag-drop inside a window do not move the window frame.
        if (!sizeSame)
        {
            // User is resizing the foreign window — cancel dock tracking.
            ClearDockDrag();
            if (_dropHighlight)
            {
                try { BeginInvoke(() => SetDropHighlight(false)); }
                catch (InvalidOperationException) { /* shutting down */ }
            }
            return;
        }

        _windowDragActive = positionMoved;

        if (_windowDragActive)
        {
            // Yield to Windows Snap / Snap Layouts along screen edges.
            var over = Bounds.Contains(screenPt) && !IsInSystemSnapZone(screenPt);
            if (over != _dropHighlight)
            {
                try { BeginInvoke(() => SetDropHighlight(over)); }
                catch (InvalidOperationException) { /* shutting down */ }
            }
        }
    }

    /// <summary>
    /// True when the cursor is in a monitor edge zone where Windows shows Snap / sticky layouts.
    /// Docking must not compete there.
    /// </summary>
    private static bool IsInSystemSnapZone(Point screenPt)
    {
        var screen = Screen.FromPoint(screenPt);
        var wa = screen.WorkingArea;
        var bounds = screen.Bounds;

        // Left / right snap (half screen) — use working area.
        if (screenPt.X <= wa.Left + SystemSnapZone) return true;
        if (screenPt.X >= wa.Right - SystemSnapZone) return true;

        // Top snap / Snap Layouts picker — use full bounds (includes under top edge).
        if (screenPt.Y <= bounds.Top + SystemSnapZone) return true;

        // Corner zones are already covered by left/right + top.
        return false;
    }

    private void SyncMaximizeFromSystem()
    {
        if (_isFullscreen) return;

        if (WindowState == FormWindowState.Maximized)
        {
            if (!_isMaximized)
            {
                _isMaximized = true;
                _dotTip.SetToolTip(_maximizeDot, "Restore");
            }
            return;
        }

        // Snapped or restored via Windows — match green-dot state when we fill the work area.
        if (!_isMaximized) return;
        var wa = Screen.FromControl(this).WorkingArea;
        if (Bounds != wa && WindowState == FormWindowState.Normal)
        {
            _isMaximized = false;
            _dotTip.SetToolTip(_maximizeDot, "Maximize");
        }
    }

    private static bool IsForeignResizeHit(int ht) => ht is
        NativeMethods.HTLEFT or NativeMethods.HTRIGHT or NativeMethods.HTTOP or NativeMethods.HTBOTTOM
        or NativeMethods.HTTOPLEFT or NativeMethods.HTTOPRIGHT
        or NativeMethods.HTBOTTOMLEFT or NativeMethods.HTBOTTOMRIGHT;

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "...";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UninstallMouseHook();
            _pruneTimer.Dispose();
            _chromeTimer.Dispose();
            _menu.Dispose();
            _tray.Dispose();
            _tabTip.Dispose();
            _dotTip.Dispose();
            _host?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Small macOS-style traffic-light circle.</summary>
internal sealed class TrafficDot : Control
{
    private readonly Color _fill;
    private bool _hover;

    public TrafficDot(Color fill)
    {
        _fill = fill;
        Size = new Size(14, 14);
        MinimumSize = Size;
        MaximumSize = Size;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Width - 3, Height - 3);
        using var brush = new SolidBrush(_hover ? ControlPaint.Light(_fill) : _fill);
        e.Graphics.FillEllipse(brush, rect);
        using var pen = new Pen(Color.FromArgb(60, 0, 0, 0), 1f);
        e.Graphics.DrawEllipse(pen, rect);
    }
}

/// <summary>Square full-screen toggle at the bottom of the side bar.</summary>
internal sealed class FullscreenButton : Control
{
    private bool _hover;
    private bool _isFullscreen;

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (_isFullscreen == value) return;
            _isFullscreen = value;
            Invalidate();
        }
    }

    public FullscreenButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = _hover ? Color.FromArgb(220, 228, 240) : Color.FromArgb(160, 168, 180);
        using var pen = new Pen(color, 1.6f);

        if (!_isFullscreen)
        {
            // Enter full screen: square with corner expansion ticks.
            var box = new Rectangle(4, 4, Width - 9, Height - 9);
            e.Graphics.DrawRectangle(pen, box);
            e.Graphics.DrawLine(pen, 1, 6, 1, 1);
            e.Graphics.DrawLine(pen, 1, 1, 6, 1);
            e.Graphics.DrawLine(pen, Width - 2, 6, Width - 2, 1);
            e.Graphics.DrawLine(pen, Width - 2, 1, Width - 7, 1);
            e.Graphics.DrawLine(pen, 1, Height - 7, 1, Height - 2);
            e.Graphics.DrawLine(pen, 1, Height - 2, 6, Height - 2);
            e.Graphics.DrawLine(pen, Width - 2, Height - 7, Width - 2, Height - 2);
            e.Graphics.DrawLine(pen, Width - 2, Height - 2, Width - 7, Height - 2);
        }
        else
        {
            // Exit full screen: inward corners.
            e.Graphics.DrawLine(pen, 1, 6, 6, 6);
            e.Graphics.DrawLine(pen, 6, 6, 6, 1);
            e.Graphics.DrawLine(pen, Width - 7, 1, Width - 7, 6);
            e.Graphics.DrawLine(pen, Width - 7, 6, Width - 2, 6);
            e.Graphics.DrawLine(pen, 1, Height - 7, 6, Height - 7);
            e.Graphics.DrawLine(pen, 6, Height - 7, 6, Height - 2);
            e.Graphics.DrawLine(pen, Width - 7, Height - 2, Width - 7, Height - 7);
            e.Graphics.DrawLine(pen, Width - 7, Height - 7, Width - 2, Height - 7);
        }
    }
}

/// <summary>Gear icon for settings at the bottom of the side bar.</summary>
internal sealed class SettingsButton : Control
{
    private bool _hover;

    public SettingsButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var color = _hover ? Color.FromArgb(220, 228, 240) : Color.FromArgb(160, 168, 180);
        using var pen = new Pen(color, 1.5f);

        float cx = Width / 2f;
        float cy = Height / 2f;
        float outer = Math.Min(Width, Height) / 2f - 2.5f;
        float inner = outer * 0.42f;

        // Simple gear: outer circle, inner hole, and 6 tooth notches as short radial ticks.
        e.Graphics.DrawEllipse(pen, cx - outer, cy - outer, outer * 2, outer * 2);
        e.Graphics.DrawEllipse(pen, cx - inner, cy - inner, inner * 2, inner * 2);

        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3.0;
            float dx = (float)Math.Cos(angle);
            float dy = (float)Math.Sin(angle);
            float x1 = cx + dx * (outer - 0.5f);
            float y1 = cy + dy * (outer - 0.5f);
            float x2 = cx + dx * (outer + 2.2f);
            float y2 = cy + dy * (outer + 2.2f);
            e.Graphics.DrawLine(pen, x1, y1, x2, y2);
        }
    }
}
