using System.Drawing.Drawing2D;

namespace WindowPane;

/// <summary>Borderless modern settings modal with backdrop fade.</summary>
internal sealed class SettingsForm : Form
{
    private readonly ComboBox _emptyMode;
    private readonly TextBox _customText;
    private readonly CheckBox _showBorder;
    private readonly ComboBox _theme;
    private readonly AppSettings _settings;
    private readonly Panel _card;
    private Point _dragOffset;
    private bool _dragging;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Settings";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(480, 560);
        BackColor = Color.FromArgb(12, 14, 18);
        DoubleBuffered = true;
        KeyPreview = true;
        Opacity = 0.97;

        // Dim overlay fills the form; card floats on top.
        var overlay = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 20, 26)
        };
        Controls.Add(overlay);

        _card = new Panel
        {
            Size = new Size(420, 500),
            BackColor = Color.FromArgb(28, 30, 36),
            Padding = new Padding(28, 24, 28, 20)
        };
        _card.Location = new Point(
            (ClientSize.Width - _card.Width) / 2,
            (ClientSize.Height - _card.Height) / 2);
        _card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundedRect(new Rectangle(0, 0, _card.Width - 1, _card.Height - 1), 16);
            using var pen = new Pen(Color.FromArgb(55, 60, 72), 1f);
            e.Graphics.DrawPath(pen, path);
        };
        overlay.Controls.Add(_card);

        var header = new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI Semibold", 18f),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(28, 22)
        };
        var sub = new Label
        {
            Text = "Appearance and empty-pane behavior",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(140, 148, 160),
            AutoSize = true,
            Location = new Point(30, 54)
        };
        var closeX = new Label
        {
            Text = "\u2715",
            Font = new Font("Segoe UI", 11f),
            ForeColor = Color.FromArgb(140, 148, 160),
            AutoSize = true,
            Cursor = Cursors.Hand,
            Location = new Point(_card.Width - 48, 22)
        };
        closeX.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        closeX.MouseEnter += (_, _) => closeX.ForeColor = Color.White;
        closeX.MouseLeave += (_, _) => closeX.ForeColor = Color.FromArgb(140, 148, 160);

        // Drag card / form from header area
        void AttachDrag(Control c)
        {
            c.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragging = true;
                _dragOffset = e.Location;
            };
            c.MouseMove += (_, e) =>
            {
                if (!_dragging) return;
                var screen = c.PointToScreen(e.Location);
                Location = new Point(screen.X - _dragOffset.X - _card.Left, screen.Y - _dragOffset.Y - _card.Top);
            };
            c.MouseUp += (_, _) => _dragging = false;
        }
        AttachDrag(header);
        AttachDrag(sub);
        AttachDrag(_card);

        int y = 88;
        var emptyLabel = FieldLabel("Empty pane content", 28, y);
        y += 24;
        _emptyMode = ModernCombo(28, y, _card.Width - 56);
        _emptyMode.Items.AddRange(["Full help text", "Short tip", "Blank", "Custom message"]);
        _emptyMode.SelectedIndex = (int)settings.EmptyContent;
        _emptyMode.SelectedIndexChanged += (_, _) => UpdateCustomEnabled();
        y += 42;

        var customLabel = FieldLabel("Custom message", 28, y);
        y += 24;
        _customText = new TextBox
        {
            Multiline = true,
            Location = new Point(28, y),
            Size = new Size(_card.Width - 56, 72),
            Text = settings.CustomEmptyText,
            BackColor = Color.FromArgb(20, 22, 28),
            ForeColor = Color.FromArgb(230, 232, 236),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };
        y += 88;

        _showBorder = new CheckBox
        {
            Text = "Show thin window border",
            Checked = settings.ShowBorder,
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 214, 220),
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(28, y),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        y += 40;

        var themeLabel = FieldLabel("Theme", 28, y);
        y += 24;
        _theme = ModernCombo(28, y, _card.Width - 56);
        foreach (var (_, name) in ThemeCatalog.All)
            _theme.Items.Add(name);
        _theme.SelectedIndex = Math.Clamp((int)settings.Theme, 0, Math.Max(0, _theme.Items.Count - 1));
        y += 56;

        var save = ModernButton("Save", Color.FromArgb(64, 120, 210), _card.Width - 56 - 100, y, 100);
        var cancel = ModernButton("Cancel", Color.FromArgb(48, 52, 60), _card.Width - 56 - 100 - 12 - 100, y, 100);
        save.Click += (_, _) =>
        {
            ApplyToSettings();
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        var credit = new Label
        {
            Text = "Developer  ·  Dr. Psych",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(100, 108, 120),
            AutoSize = true,
            Location = new Point(28, _card.Height - 36)
        };

        _card.Controls.Add(header);
        _card.Controls.Add(sub);
        _card.Controls.Add(closeX);
        _card.Controls.Add(emptyLabel);
        _card.Controls.Add(_emptyMode);
        _card.Controls.Add(customLabel);
        _card.Controls.Add(_customText);
        _card.Controls.Add(_showBorder);
        _card.Controls.Add(themeLabel);
        _card.Controls.Add(_theme);
        _card.Controls.Add(save);
        _card.Controls.Add(cancel);
        _card.Controls.Add(credit);

        AcceptButton = save;
        CancelButton = cancel;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        UpdateCustomEnabled();
        Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 20, 20));
        _card.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, _card.Width, _card.Height, 16, 16));
    }

    private static Label FieldLabel(string text, int x, int y) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 9f),
        ForeColor = Color.FromArgb(160, 168, 180),
        AutoSize = true,
        Location = new Point(x, y)
    };

    private static ComboBox ModernCombo(int x, int y, int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        Location = new Point(x, y),
        Size = new Size(width, 28),
        Font = new Font("Segoe UI", 9.5f),
        BackColor = Color.FromArgb(20, 22, 28),
        ForeColor = Color.FromArgb(230, 232, 236)
    };

    private static Button ModernButton(string text, Color bg, int x, int y, int width) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, 36),
        FlatStyle = FlatStyle.Flat,
        BackColor = bg,
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 9.5f),
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 }
    };

    private void UpdateCustomEnabled()
    {
        _customText.Enabled = _emptyMode.SelectedIndex == (int)EmptyContentMode.Custom;
        _customText.BackColor = _customText.Enabled
            ? Color.FromArgb(20, 22, 28)
            : Color.FromArgb(24, 26, 30);
    }

    private void ApplyToSettings()
    {
        _settings.EmptyContent = (EmptyContentMode)_emptyMode.SelectedIndex;
        _settings.CustomEmptyText = _customText.Text;
        _settings.ShowBorder = _showBorder.Checked;
        _settings.Theme = (AppTheme)_theme.SelectedIndex;
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);
}
