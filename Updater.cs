using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ReforgeX;

static class Updater
{
    const string Repo = "binx-ux/ReforgeX";

    public static bool FromInstall()
    {
        var dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReforgeX");
        return string.Equals(dir, home, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<Release?> FindNewer(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReforgeX");
        var json = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var next))
            return null;
        var current = Norm(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));
        next = Norm(next);
        if (next <= current)
            return null;
        string? url = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (!string.Equals(name, "ReforgeX-Setup.exe", StringComparison.OrdinalIgnoreCase))
                    continue;
                url = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return new Release(next, url);
    }

    public static async Task<string> Download(string url, CancellationToken ct)
    {
        var dest = Path.Combine(Path.GetTempPath(), "ReforgeX-Setup.exe");
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ReforgeX");
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        await using var input = await res.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(dest);
        await input.CopyToAsync(output, ct);
        return dest;
    }

    public static void Apply(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /update=1",
            UseShellExecute = true
        });
    }

    static Version Norm(Version version)
    {
        return new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    public sealed record Release(Version Version, string Url);
}
