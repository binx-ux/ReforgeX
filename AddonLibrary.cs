using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ReforgeX;

static class AddonLibrary
{
    public const string VanillaGuid = "58D0FB3206B6F859";

    static readonly Regex FieldRe = new(
        @"\b(ID|GUID|TITLE|Version)\s+""([^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex JsonNameRe = new(
        @"""(?:name|title)""\s*:\s*""([^""\\]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string DefaultAddonsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games",
            "ArmaReforger",
            "addons");
    }

    public static List<ScanRoot> Discover(IEnumerable<string>? extra)
    {
        var list = new List<ScanRoot>();

        void Add(string? path, string label, bool userAdded, bool holdingDisabled)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (list.Any(r => string.Equals(r.Path, full, StringComparison.OrdinalIgnoreCase)))
                return;
            list.Add(new ScanRoot
            {
                Path = full,
                Label = label,
                UserAdded = userAdded,
                Locked = IsUnderGameInstall(full),
                Exists = Directory.Exists(full),
                HoldingDisabled = holdingDisabled
            });
        }

        void AddLive(string? path, string label, bool userAdded)
        {
            Add(path, label, userAdded, false);
            if (string.IsNullOrWhiteSpace(path))
                return;
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (IsUnderGameInstall(full) || label == "Workshop")
                return;
            Add(DisabledPath(full), label, userAdded, true);
        }

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        AddLive(Path.Combine(docs, "My Games", "ArmaReforger", "addons"), "Documents", false);
        AddLive(Path.Combine(docs, "My Games", "ArmaReforger", "profile", "addons"), "Profile", false);
        AddLive(Path.Combine(docs, "My Games", "ArmaReforgerWorkbench", "addons"), "Workbench", false);

        var experimental = Path.Combine(docs, "My Games", "ArmaReforgerExperimental", "addons");
        if (Directory.Exists(experimental))
            AddLive(experimental, "Experimental", false);
        var experimentalProfile = Path.Combine(docs, "My Games", "ArmaReforgerExperimental", "profile", "addons");
        if (Directory.Exists(experimentalProfile))
            AddLive(experimentalProfile, "Experimental", false);

        foreach (var lib in SteamLibraries())
            Add(Path.Combine(lib, "steamapps", "workshop", "content", "1874880"), "Workshop", false, false);

        foreach (var dir in LaunchDirs())
            AddLive(dir.Path, dir.Label, false);

        if (extra != null)
        {
            foreach (var path in extra)
                AddLive(path, "Added", true);
        }

        return list;
    }

    public static List<ModEntry> Scan(IReadOnlyList<ScanRoot> roots, IProgress<string>? progress, CancellationToken ct)
    {
        var found = new List<ModEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root.Path))
                continue;

            List<string> children;
            try
            {
                children = Directory.EnumerateDirectories(root.Path).ToList();
            }
            catch
            {
                children = new List<string>();
            }

            var mods = new List<string>();
            foreach (var child in children)
            {
                if (IsModFolder(child))
                    mods.Add(child);
            }

            if (mods.Count == 0 && IsModFolder(root.Path))
                mods.Add(root.Path);

            foreach (var dir in mods)
            {
                ct.ThrowIfCancellationRequested();
                var key = Identity(dir);
                if (!seen.Add(key))
                    continue;
                progress?.Report("Reading " + Path.GetFileName(dir));
                var mod = TryRead(dir, root.Label, root.Locked, root.HoldingDisabled);
                if (mod != null)
                    found.Add(mod);
            }
        }

        return found;
    }

    public static bool IsModFolder(string dir)
    {
        try
        {
            if (Directory.EnumerateFiles(dir, "*.gproj").Any())
                return true;
            if (Directory.EnumerateFiles(dir, "*.pak").Any())
                return true;
            var name = Path.GetFileName(dir);
            if (IsHex16(name) && Directory.EnumerateFileSystemEntries(dir).Any())
                return true;
        }
        catch
        {
        }
        return false;
    }

    public static string DisabledPath(string addonRoot)
    {
        var full = Path.GetFullPath(addonRoot);
        var parent = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (string.IsNullOrEmpty(parent))
            return full + "-disabled";
        return Path.Combine(parent, name + "-disabled");
    }

    public static void SetEnabled(ModEntry mod, bool enabled)
    {
        if (mod.Locked || IsUnderGameInstall(mod.FolderPath))
            throw new IOException("That one stays with the game.");
        if (string.Equals(mod.Source, "Workshop", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Workshop mods stay where Steam put them.");
        if (mod.Disabled == !enabled)
            return;

        var parent = Path.GetDirectoryName(mod.FolderPath);
        if (string.IsNullOrEmpty(parent))
            throw new IOException("Refusing to move that folder.");

        string destParent;
        var parentName = Path.GetFileName(parent);
        if (parentName.EndsWith("-disabled", StringComparison.OrdinalIgnoreCase))
        {
            var baseName = parentName.Substring(0, parentName.Length - "-disabled".Length);
            var above = Path.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(above) || baseName.Length == 0)
                throw new IOException("Refusing to move that folder.");
            destParent = Path.Combine(above, baseName);
        }
        else
        {
            destParent = DisabledPath(parent);
        }

        if (!RecycleBin.Safe(mod.FolderPath))
            throw new IOException("Refusing to move that folder.");

        Directory.CreateDirectory(destParent);
        var dest = Path.Combine(destParent, mod.FolderName);
        if (Directory.Exists(dest))
            throw new IOException(mod.Title + " is already in the other folder.");
        Directory.Move(mod.FolderPath, dest);
    }

    public static ModEntry? TryRead(string dir, string source, bool locked, bool disabled)
    {
        if (!IsModFolder(dir))
            return null;

        string id = "";
        string guid = "";
        string title = "";
        string version = "";
        string? gproj = null;
        try
        {
            gproj = Directory.EnumerateFiles(dir, "*.gproj").FirstOrDefault();
        }
        catch
        {
            return null;
        }

        if (gproj != null)
            ReadProject(gproj, ref id, ref guid, ref title, ref version);

        if (string.Equals(guid, VanillaGuid, StringComparison.OrdinalIgnoreCase))
            return null;

        var folder = Path.GetFileName(dir);
        if (title.Length == 0)
            title = NameFromJson(dir);
        if (title.Length == 0)
            title = folder;
        if (guid.Length == 0 && IsHex16(folder))
            guid = folder;

        return new ModEntry
        {
            Title = title,
            Guid = guid,
            ProjectId = id,
            Version = version,
            FolderName = folder,
            FolderPath = dir,
            Source = source,
            Locked = locked || IsUnderGameInstall(dir),
            Disabled = disabled
        };
    }

    public static long Measure(string path)
    {
        return FolderBytes(path);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
            bytes = 0;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double n = bytes;
        int i = 0;
        while (n >= 1024 && i < units.Length - 1)
        {
            n /= 1024;
            i++;
        }
        if (i == 0)
            return ((long)n).ToString("0") + " B";
        return n.ToString("0.0") + " " + units[i];
    }

    public static bool IsUnderGameInstall(string path)
    {
        return path.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Program Files", StringComparison.OrdinalIgnoreCase);
    }

    static void ReadProject(string gproj, ref string id, ref string guid, ref string title, ref string version)
    {
        string text;
        try
        {
            text = ReadHead(gproj, 262144);
        }
        catch
        {
            return;
        }

        foreach (Match m in FieldRe.Matches(text))
        {
            var key = m.Groups[1].Value;
            var val = m.Groups[2].Value;
            if (key.Equals("ID", StringComparison.OrdinalIgnoreCase) && id.Length == 0)
                id = val;
            else if (key.Equals("GUID", StringComparison.OrdinalIgnoreCase) && guid.Length == 0)
                guid = val;
            else if (key.Equals("TITLE", StringComparison.OrdinalIgnoreCase) && title.Length == 0)
                title = val;
            else if (key.Equals("Version", StringComparison.OrdinalIgnoreCase) && version.Length == 0)
                version = val;
        }
    }

    static string NameFromJson(string dir)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.json");
        }
        catch
        {
            return "";
        }

        foreach (var file in files)
        {
            string text;
            try
            {
                text = ReadHead(file, 65536);
            }
            catch
            {
                continue;
            }
            var m = JsonNameRe.Match(text);
            if (m.Success && m.Groups[1].Value.Trim().Length > 0)
                return m.Groups[1].Value.Trim();
        }
        return "";
    }

    static long FolderBytes(string path)
    {
        long total = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            });
        }
        catch
        {
            return 0;
        }

        foreach (var file in files)
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
            }
        }
        return total;
    }

    static string Identity(string path)
    {
        try
        {
            var link = new DirectoryInfo(path).ResolveLinkTarget(true);
            if (link != null)
                return Path.GetFullPath(link.FullName);
        }
        catch
        {
        }
        return Path.GetFullPath(path);
    }

    static bool IsHex16(string s)
    {
        if (s.Length != 16)
            return false;
        foreach (var c in s)
        {
            bool hex = (c >= '0' && c <= '9')
                || (c >= 'a' && c <= 'f')
                || (c >= 'A' && c <= 'F');
            if (!hex)
                return false;
        }
        return true;
    }

    static string ReadHead(string path, int max)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int n = (int)Math.Min(max, fs.Length);
        var buf = new byte[n];
        int read = 0;
        while (read < n)
        {
            int got = fs.Read(buf, read, n - read);
            if (got == 0)
                break;
            read += got;
        }
        if (read >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
            return Encoding.Unicode.GetString(buf, 0, read);
        if (read >= 2 && buf[0] == 0xFE && buf[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(buf, 0, read);
        if (read >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
            return Encoding.UTF8.GetString(buf, 3, read - 3);
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    static IEnumerable<string> SteamLibraries()
    {
        var steam = SteamRoot();
        if (steam == null)
            yield break;
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;
        string text;
        try
        {
            text = File.ReadAllText(vdf);
        }
        catch
        {
            yield break;
        }
        foreach (Match m in Regex.Matches(text, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase))
            yield return Clean(m.Groups[1].Value);
    }

    static IEnumerable<(string Path, string Label)> LaunchDirs()
    {
        var steam = SteamRoot();
        if (steam == null)
            yield break;
        var userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata))
            yield break;

        foreach (var userDir in Directory.EnumerateDirectories(userdata))
        {
            var cfg = Path.Combine(userDir, "config", "localconfig.vdf");
            if (!File.Exists(cfg))
                continue;
            string text;
            try
            {
                text = File.ReadAllText(cfg);
            }
            catch
            {
                continue;
            }

            foreach (var raw in MatchArg(text, "addonDownloadDir"))
            {
                var path = Clean(raw);
                if (path.EndsWith("addons", StringComparison.OrdinalIgnoreCase))
                    yield return (path, "Download folder");
                else
                    yield return (Path.Combine(path, "addons"), "Download folder");
            }

            foreach (var raw in MatchArg(text, "addonsDir"))
            {
                foreach (var part in Clean(raw).Split(','))
                {
                    var p = part.Trim().Trim('"');
                    if (p.Length > 0)
                        yield return (p, "Launch option");
                }
            }
        }
    }

    static IEnumerable<string> MatchArg(string text, string name)
    {
        var patterns = new[]
        {
            "-" + name + @"\s+\\""(.+?)\\""",
            "-" + name + @"\s+""([^""]+)"""
        };
        foreach (var pattern in patterns)
        {
            foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                yield return m.Groups[1].Value;
        }
    }

    static string? SteamRoot()
    {
        string? found = ReadReg(RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Valve\Steam", "SteamPath");
        found ??= ReadReg(RegistryHive.CurrentUser, RegistryView.Registry32, @"Software\Valve\Steam", "SteamPath");
        found ??= ReadReg(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        found ??= ReadReg(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath");
        if (string.IsNullOrWhiteSpace(found))
            return null;
        try
        {
            var full = Path.GetFullPath(found);
            return Directory.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }

    static string? ReadReg(RegistryHive hive, RegistryView view, string sub, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(sub);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    static string Clean(string raw)
    {
        return raw.Trim().Replace(@"\\", @"\").Replace('/', '\\');
    }
}
