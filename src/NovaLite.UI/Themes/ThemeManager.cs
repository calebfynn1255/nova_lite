using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;

namespace NovaLite.UI.Themes;

public enum AppTheme { Light, Dark, System, OledBlack }

/// <summary>
/// Manages runtime theme switching by swapping the active ResourceDictionary
/// in <see cref="Application.Resources.MergedDictionaries"/>.
/// </summary>
public static class ThemeManager
{
    private const string ThemeUriBase = "avares://NovaLite/Themes/";
    private static AppTheme _current = AppTheme.Dark;

    public static AppTheme Current => _current;

    /// <summary>Applies a theme by name ("Light", "Dark", "System", "OledBlack").</summary>
    public static void Apply(string themeName)
    {
        var theme = themeName switch
        {
            "Light"     => AppTheme.Light,
            "System"    => AppTheme.System,
            "OledBlack" => AppTheme.OledBlack,
            _           => AppTheme.Dark
        };
        Apply(theme);
    }

    public static void Apply(AppTheme theme)
    {
        _current = theme;
        var app = Application.Current!;

        // 1. Set Avalonia base variant (controls built-in Fluent styles)
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light  => ThemeVariant.Light,
            AppTheme.System => ThemeVariant.Default,
            _               => ThemeVariant.Dark   // Dark + OledBlack both use dark Fluent
        };

        // 2. For System, resolve actual variant from OS
        var effectiveTheme = theme == AppTheme.System
            ? ResolveSystemTheme()
            : theme;

        // 3. Swap our custom resource dictionary
        SwapThemeDictionary(effectiveTheme);

        // 4. Subscribe to system colour changes when in System mode
        if (theme == AppTheme.System)
            SubscribeToSystemChanges();
    }

    private static void SwapThemeDictionary(AppTheme theme)
    {
        var app = Application.Current!;
        if (app.Resources.MergedDictionaries.Count > 0)
        {
            app.Resources.MergedDictionaries.Clear();
        }

        // Add the new one
        var uri = new Uri(ThemeUriBase + GetFileName(theme));
        var include = new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://NovaLite/App.xaml")) { Source = uri };
        app.Resources.MergedDictionaries.Add(include);
    }

    private static string GetFileName(AppTheme theme) => theme switch
    {
        AppTheme.Light     => "Light.axaml",
        AppTheme.OledBlack => "OledBlack.axaml",
        _                  => "Dark.axaml"
    };

    private static AppTheme ResolveSystemTheme()
    {
        try
        {
            var settings = Application.Current?.PlatformSettings;
            if (settings is null) return AppTheme.Dark;
            return settings.GetColorValues().ThemeVariant == PlatformThemeVariant.Light
                ? AppTheme.Light
                : AppTheme.Dark;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }

    private static bool _subscribed;
    private static void SubscribeToSystemChanges()
    {
        if (_subscribed) return;
        _subscribed = true;
        var settings = Application.Current?.PlatformSettings;
        if (settings is null) return;
        settings.ColorValuesChanged += (_, e) =>
        {
            if (_current != AppTheme.System) return;
            var resolved = e.ThemeVariant == PlatformThemeVariant.Light
                ? AppTheme.Light
                : AppTheme.Dark;
            SwapThemeDictionary(resolved);
        };
    }
}
