using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowPane;

internal enum EmptyContentMode
{
    FullHelp = 0,
    ShortTip = 1,
    Blank = 2,
    Custom = 3
}

internal enum AppTheme
{
    Dark = 0,
    Midnight = 1,
    Slate = 2,
    Light = 3,
    Nord = 4
}

internal sealed class AppSettings
{
    public EmptyContentMode EmptyContent { get; set; } = EmptyContentMode.FullHelp;
    public string CustomEmptyText { get; set; } = "Drop a window here";
    public bool ShowBorder { get; set; } = true;
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WindowPane",
            "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public string GetEmptyHintText() => EmptyContent switch
    {
        EmptyContentMode.ShortTip =>
            "Drag a window here by its title bar to dock it.",
        EmptyContentMode.Blank =>
            "",
        EmptyContentMode.Custom =>
            string.IsNullOrWhiteSpace(CustomEmptyText) ? " " : CustomEmptyText,
        _ =>
            "Drag a window by its title bar onto this pane and release.\n" +
            "It leaves the taskbar / Alt+Tab and lives only here.\n\n" +
            "Close this pane to restore apps exactly as they were.\n" +
            "Drag the left bar to move  ·  Green maximizes  ·  Bottom icons: full screen & settings"
    };
}

internal readonly record struct ThemeColors(
    Color Border,
    Color Content,
    Color SideBar,
    Color TabBar,
    Color HintText,
    Color HintTextHighlight,
    Color DropHighlight);

internal static class ThemeCatalog
{
    public static IReadOnlyList<(AppTheme Id, string Name)> All { get; } =
    [
        (AppTheme.Dark, "Dark"),
        (AppTheme.Midnight, "Midnight"),
        (AppTheme.Slate, "Slate"),
        (AppTheme.Light, "Light"),
        (AppTheme.Nord, "Nord")
    ];

    public static ThemeColors Get(AppTheme theme) => theme switch
    {
        AppTheme.Midnight => new ThemeColors(
            Border: Color.FromArgb(55, 65, 90),
            Content: Color.FromArgb(12, 14, 24),
            SideBar: Color.FromArgb(8, 10, 18),
            TabBar: Color.FromArgb(8, 10, 18),
            HintText: Color.FromArgb(140, 150, 175),
            HintTextHighlight: Color.FromArgb(190, 205, 240),
            DropHighlight: Color.FromArgb(28, 40, 70)),

        AppTheme.Slate => new ThemeColors(
            Border: Color.FromArgb(100, 110, 120),
            Content: Color.FromArgb(40, 44, 52),
            SideBar: Color.FromArgb(32, 36, 42),
            TabBar: Color.FromArgb(32, 36, 42),
            HintText: Color.FromArgb(180, 186, 194),
            HintTextHighlight: Color.FromArgb(220, 226, 235),
            DropHighlight: Color.FromArgb(55, 70, 90)),

        AppTheme.Light => new ThemeColors(
            Border: Color.FromArgb(180, 184, 190),
            Content: Color.FromArgb(245, 246, 248),
            SideBar: Color.FromArgb(232, 234, 238),
            TabBar: Color.FromArgb(232, 234, 238),
            HintText: Color.FromArgb(90, 96, 108),
            HintTextHighlight: Color.FromArgb(40, 80, 140),
            DropHighlight: Color.FromArgb(220, 230, 245)),

        AppTheme.Nord => new ThemeColors(
            Border: Color.FromArgb(76, 86, 106),
            Content: Color.FromArgb(46, 52, 64),
            SideBar: Color.FromArgb(59, 66, 82),
            TabBar: Color.FromArgb(59, 66, 82),
            HintText: Color.FromArgb(216, 222, 233),
            HintTextHighlight: Color.FromArgb(136, 192, 208),
            DropHighlight: Color.FromArgb(67, 76, 94)),

        _ => new ThemeColors(
            Border: Color.FromArgb(90, 96, 108),
            Content: Color.FromArgb(22, 24, 28),
            SideBar: Color.FromArgb(18, 19, 22),
            TabBar: Color.FromArgb(18, 19, 22),
            HintText: Color.FromArgb(160, 168, 180),
            HintTextHighlight: Color.FromArgb(210, 225, 255),
            DropHighlight: Color.FromArgb(36, 52, 78))
    };
}
