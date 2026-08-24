using System.Windows;

namespace MediaFetch.Desktop.Services;

public sealed class ThemeService
{
    public const string Dark = "Dark";
    public const string Light = "Light";

    public string Apply(string? requestedTheme)
    {
        var theme = string.Equals(requestedTheme, Light, StringComparison.OrdinalIgnoreCase)
            ? Light
            : Dark;

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var currentTheme = dictionaries.FirstOrDefault(IsThemeDictionary);
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"Themes/{theme}Theme.xaml", UriKind.Relative)
        };

        if (currentTheme is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            var index = dictionaries.IndexOf(currentTheme);
            dictionaries[index] = replacement;
        }

        return theme;
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;

        return source?.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) == true
            || source?.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) == true;
    }
}
