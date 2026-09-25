using System.Windows;
using System.Windows.Media;
using LlmUsageMonitor.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace LlmUsageMonitor.App.Infrastructure;

/// <summary>Applies light/dark palettes and per-module accent colors to the app resources.</summary>
public sealed class ThemeManager
{
    public const string KeyBackground = "App.Background";
    public const string KeyCard = "App.Card";
    public const string KeyCardAlt = "App.CardAlt";
    public const string KeyBorder = "App.Border";
    public const string KeyText = "App.Text";
    public const string KeySubText = "App.SubText";
    public const string KeyAccent = "App.Accent";
    public const string KeyAccentSoft = "App.AccentSoft";
    public const string KeyNavSelected = "App.NavSelected";

    public ThemeMode Mode { get; private set; } = ThemeMode.Dark;
    public string CurrentModule { get; private set; } = ModuleKeys.Dashboard;
    private ThemeSettings _theme = new();

    public void Initialize(ThemeSettings theme)
    {
        _theme = theme;
        Apply(theme.Mode);
    }

    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        var effective = mode == ThemeMode.System ? DetectSystem() : mode;
        var isDark = effective != ThemeMode.Light;

        try
        {
            ApplicationThemeManager.Apply(
                isDark ? ApplicationTheme.Dark : ApplicationTheme.Light,
                WindowBackdropType.Mica,
                true);
        }
        catch
        {
            // Backdrop not supported on this system; palette still applies.
        }

        var resources = Application.Current.Resources;
        Set(resources, KeyBackground, isDark ? "#0F1115" : "#F3F5F9");
        Set(resources, KeyCard, isDark ? "#171A21" : "#FFFFFF");
        Set(resources, KeyCardAlt, isDark ? "#1E222C" : "#F7F9FC");
        Set(resources, KeyBorder, isDark ? "#2A303C" : "#E2E6EE");
        Set(resources, KeyText, isDark ? "#F3F6FB" : "#171B23");
        Set(resources, KeySubText, isDark ? "#9AA4B2" : "#5B6472");
        SetModuleAccent(CurrentModule);
    }

    public void SetModuleAccent(string module)
    {
        CurrentModule = module;
        var hex = _theme.ColorFor(module);
        var resources = Application.Current.Resources;
        Set(resources, KeyAccent, hex);
        Set(resources, KeyAccentSoft, SoftenColor(hex));
        Set(resources, KeyNavSelected, SoftenColor(hex, 0.22));
    }

    private static ThemeMode DetectSystem()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return value is int i && i == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch
        {
            return ThemeMode.Dark;
        }
    }

    private static void Set(ResourceDictionary resources, string key, string hex)
        => resources[key] = CreateBrush(hex);

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>Returns a low-opacity variant (#AARRGGBB) of a color for tinted backgrounds.</summary>
    public static string SoftenColor(string hex, double opacity = 0.16)
    {
        var rgb = hex.TrimStart('#');
        if (rgb.Length > 6) rgb = rgb[..6];
        return $"#{(int)(opacity * 255):X2}{rgb}";
    }

    public static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
