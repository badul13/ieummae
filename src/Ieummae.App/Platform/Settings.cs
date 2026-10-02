using System.Text;

namespace Ieummae.App.Platform;

// 앱 설정 - %APPDATA%\ieummae\settings.txt, 줄마다 키=값
public static class Settings
{
    static string File => AppData.PathOf("settings.txt");
    static Dictionary<string, string>? s_values;

    static Dictionary<string, string> Values
    {
        get
        {
            if (s_values is not null) return s_values;
            s_values = [];
            try
            {
                if (System.IO.File.Exists(File))
                    foreach (var line in System.IO.File.ReadAllLines(File))
                        if (line.IndexOf('=') is > 0 and var i) s_values[line[..i].Trim()] = line[(i + 1)..].Trim();
            }
            catch (IOException) { }
            return s_values;
        }
    }

    static void Save()
    {
        try { System.IO.File.WriteAllLines(File, Values.Select(kv => $"{kv.Key}={kv.Value}"), new UTF8Encoding(false)); }
        catch (IOException) { }
    }

    static string Get(string key, string def) => Values.TryGetValue(key, out var v) ? v : def;
    static void Set(string key, string value) { Values[key] = value; Save(); }

    // 테마 - system / light / dark
    public static string Theme { get => Get("theme", "system"); set => Set("theme", value); }
    public static bool DiffSplit { get => Get("diff.split", "false") == "true"; set => Set("diff.split", value ? "true" : "false"); }
    public static bool DiffIgnoreWhitespace { get => Get("diff.ignoreWhitespace", "false") == "true"; set => Set("diff.ignoreWhitespace", value ? "true" : "false"); }
}
