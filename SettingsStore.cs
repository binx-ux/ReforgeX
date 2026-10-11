using System.Text.Json;

namespace ReforgeX;

sealed class SettingsStore
{
    public List<string> ExtraRoots { get; set; } = new();
    public string ImportRoot { get; set; } = "";

    static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static SettingsStore Load()
    {
        try
        {
            var path = Location();
            if (!File.Exists(path))
                return new SettingsStore();
            var loaded = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(path), Opt);
            if (loaded == null)
                return new SettingsStore();
            loaded.ExtraRoots ??= new List<string>();
            loaded.ImportRoot ??= "";
            return loaded;
        }
        catch
        {
            return new SettingsStore();
        }
    }

    public void Save()
    {
        var path = Location();
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Opt));
    }

    static string Location()
    {
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReforgeX",
            "settings.json");
    }
}
