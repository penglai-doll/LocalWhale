using LocalWhale.Core.Models;
using Microsoft.UI.Xaml;

namespace LocalWhale.App;

internal sealed class ShellThemeService
{
    private static readonly IReadOnlyDictionary<VisualTheme, Uri> ThemeUris =
        new Dictionary<VisualTheme, Uri>
        {
            [VisualTheme.Original] = new("ms-appx:///Themes/OriginalTheme.xaml", UriKind.Absolute),
            [VisualTheme.WhaleGirl] = new("ms-appx:///Themes/WhaleGirlTheme.xaml", UriKind.Absolute)
        };

    private ResourceDictionary _activeDictionary;

    public ShellThemeService()
    {
        _activeDictionary = Application.Current.Resources.MergedDictionaries
            .FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.EndsWith(
                    "Themes/OriginalTheme.xaml",
                    StringComparison.OrdinalIgnoreCase) == true)
            ?? throw new InvalidOperationException("Original shell theme was not loaded by App.xaml.");
    }

    public VisualTheme CurrentTheme { get; private set; } = VisualTheme.Original;

    public event EventHandler<VisualTheme>? ThemeChanged;

    public void Apply(VisualTheme theme)
    {
        if (!ThemeUris.TryGetValue(theme, out var source))
        {
            theme = VisualTheme.Original;
            source = ThemeUris[theme];
        }

        if (CurrentTheme == theme &&
            _activeDictionary.Source?.OriginalString.EndsWith(
                source.AbsolutePath,
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        var replacement = new ResourceDictionary { Source = source };
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var activeIndex = dictionaries.IndexOf(_activeDictionary);
        if (activeIndex >= 0)
        {
            dictionaries[activeIndex] = replacement;
        }
        else
        {
            dictionaries.Add(replacement);
        }

        _activeDictionary = replacement;
        CurrentTheme = theme;
        ThemeChanged?.Invoke(this, theme);
    }
}
