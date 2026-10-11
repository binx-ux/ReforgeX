using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ReforgeX;

sealed class Incoming
{
    public string Folder { get; init; } = "";
    public string Title { get; init; } = "";
    public string Prefix { get; init; } = "";
    public string SourcePath { get; init; } = "";
}

sealed class PackManifest
{
    public string Kind { get; set; } = "";
    public List<PackMod> Mods { get; set; } = new();
}

sealed class PackMod
{
    public string Folder { get; set; } = "";
    public string Title { get; set; } = "";
    public string Guid { get; set; } = "";
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
}

static class Packer
{
    public const string Kind = "reforgex-addons";

    static readonly JsonSerializerOptions JsonOpt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static void Export(IReadOnlyList<ModEntry> mods, string zipPath, IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = new List<(ModEntry Mod, string Folder)>();
        foreach (var mod in mods)
            listed.Add((mod, Unique(used, string.IsNullOrWhiteSpace(mod.FolderName) ? mod.Title : mod.FolderName)));

        var manifest = new PackManifest
        {
            Kind = Kind,
            Mods = listed.Select(x => new PackMod
            {
                Folder = x.Folder,
                Title = x.Mod.Title,
                Guid = x.Mod.Guid,
                Id = x.Mod.ProjectId,
                Version = x.Mod.Version
            }).ToList()
        };

        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var entry = zip.CreateEntry("pack.json", CompressionLevel.Fastest);
            using (var writer = new StreamWriter(entry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest, JsonOpt));

            for (int i = 0; i < listed.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var mod = listed[i].Mod;
                var folder = listed[i].Folder;
                progress?.Report("Packing " + (i + 1) + " of " + listed.Count + ": " + mod.Title);
                foreach (var file in Directory.EnumerateFiles(mod.FolderPath, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }))
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(mod.FolderPath, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, folder + "/" + rel, CompressionLevel.NoCompression);
                }
            }
        }
        catch
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
            }
            catch
            {
            }
            throw;
        }
    }

    public static List<Incoming> InspectZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        var fromPack = ReadManifest(zip);
        if (fromPack.Count > 0)
            return fromPack;

        var result = new List<Incoming>();
        var tops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (name.Equals("pack.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var slash = name.IndexOf('/');
            if (slash <= 0)
                continue;
            tops.Add(name[..slash]);
        }

        foreach (var top in tops.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var prefix = top + "/";
            if (!HasAddonFile(names, prefix))
                continue;
            var title = TitleFromZip(zip, prefix) ?? top;
            result.Add(new Incoming
            {
                Folder = SafeFolder(top),
                Title = title,
                Prefix = prefix
            });
        }

        if (result.Count == 0)
        {
            bool rootProject = names.Any(n =>
                n.EndsWith(".gproj", StringComparison.OrdinalIgnoreCase) && !n.Contains('/'));
            bool rootPak = names.Any(n =>
                n.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !n.Contains('/'));
            if (rootProject || rootPak)
            {
                var title = TitleFromZip(zip, "") ?? "addon";
                result.Add(new Incoming
                {
                    Folder = SafeFolder(title),
                    Title = title,
                    Prefix = ""
                });
            }
        }

        return result;
    }

    public static List<Incoming> InspectFolder(string path)
    {
        var result = new List<Incoming>();
        if (AddonLibrary.IsModFolder(path))
        {
            var one = FromDisk(path);
            if (one != null)
                result.Add(one);
            return result;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(path);
        }
        catch
        {
            return result;
        }

        foreach (var child in children)
        {
            if (!AddonLibrary.IsModFolder(child))
                continue;
            var item = FromDisk(child);
            if (item != null)
                result.Add(item);
        }
        return result;
    }

    public static int Import(string? zipPath, IReadOnlyList<Incoming> items, string destRoot, bool replace, IProgress<string>? progress, CancellationToken ct)
    {
        var rootFull = Path.GetFullPath(destRoot);
        Directory.CreateDirectory(rootFull);
        int wrote = 0;

        ZipArchive? zip = null;
        if (!string.IsNullOrEmpty(zipPath))
            zip = ZipFile.OpenRead(zipPath);

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];
                progress?.Report("Importing " + (i + 1) + " of " + items.Count + ": " + item.Title);
                var target = Path.GetFullPath(Path.Combine(rootFull, item.Folder));
                if (!Inside(rootFull, target))
                    throw new IOException("Bad folder name in the pack.");

                if (!string.IsNullOrEmpty(item.SourcePath))
                {
                    var source = Path.GetFullPath(item.SourcePath);
                    if (Inside(source, target) || Inside(target, source))
                        throw new IOException("Pick a different folder than the one you are importing.");
                }

                if (Directory.Exists(target))
                {
                    if (!replace)
                        continue;
                    RecycleBin.Delete(target);
                }

                if (!string.IsNullOrEmpty(item.SourcePath))
                    CopyTree(item.SourcePath, target, ct);
                else if (zip != null)
                    ExtractPrefix(zip, item.Prefix, target, ct);
                wrote++;
            }
        }
        finally
        {
            zip?.Dispose();
        }
        return wrote;
    }

    static List<Incoming> ReadManifest(ZipArchive zip)
    {
        var result = new List<Incoming>();
        var pack = zip.GetEntry("pack.json");
        if (pack == null)
            return result;
        PackManifest? manifest;
        using (var reader = new StreamReader(pack.Open()))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<PackManifest>(reader.ReadToEnd(), JsonOpt);
            }
            catch
            {
                return result;
            }
        }
        if (manifest == null || !string.Equals(manifest.Kind, Kind, StringComparison.OrdinalIgnoreCase))
            return result;
        foreach (var mod in manifest.Mods)
        {
            if (string.IsNullOrWhiteSpace(mod.Folder))
                continue;
            var folder = SafeFolder(mod.Folder);
            result.Add(new Incoming
            {
                Folder = folder,
                Title = string.IsNullOrWhiteSpace(mod.Title) ? folder : mod.Title,
                Prefix = folder + "/"
            });
        }
        return result;
    }

    static Incoming? FromDisk(string dir)
    {
        var mod = AddonLibrary.TryRead(dir, "", false);
        if (mod == null)
            return null;
        var folder = SafeFolder(string.IsNullOrWhiteSpace(mod.FolderName) ? mod.Title : mod.FolderName);
        return new Incoming
        {
            Folder = folder,
            Title = mod.Title,
            SourcePath = dir
        };
    }

    static bool HasAddonFile(List<string> names, string prefix)
    {
        foreach (var name in names)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.EndsWith(".gproj", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string? TitleFromZip(ZipArchive zip, string prefix)
    {
        ZipArchiveEntry? found = null;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.EndsWith(".gproj", StringComparison.OrdinalIgnoreCase))
                continue;
            found = entry;
            var rest = prefix.Length == 0 ? name : name[prefix.Length..];
            if (!rest.Contains('/'))
                break;
        }
        if (found == null)
            return null;
        using var reader = new StreamReader(found.Open());
        var text = reader.ReadToEnd();
        if (text.Length > 262144)
            text = text[..262144];
        var match = Regex.Match(text, @"\bTITLE\s+""([^""]*)""", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var title = match.Groups[1].Value.Trim();
        return title.Length == 0 ? null : title;
    }

    static void ExtractPrefix(ZipArchive zip, string prefix, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = prefix.Length == 0 ? name : name[prefix.Length..];
            if (rel.Length == 0 || rel.Equals("pack.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var outPath = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(target, outPath))
                continue;
            if (name.EndsWith("/"))
            {
                Directory.CreateDirectory(outPath);
                continue;
            }
            var parent = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            entry.ExtractToFile(outPath, true);
        }
    }

    static void CopyTree(string source, string dest, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        }))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(source, file);
            var target = Path.GetFullPath(Path.Combine(dest, rel));
            if (!Inside(dest, target))
                continue;
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, target, true);
        }
    }

    public static string SafeFolder(string name)
    {
        var cleaned = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(c, '_');
        cleaned = cleaned.Replace("..", "_").Trim(' ', '.');
        if (cleaned.Length == 0)
            cleaned = "addon";
        return cleaned;
    }

    static string Unique(HashSet<string> used, string name)
    {
        var cleaned = SafeFolder(name);
        if (used.Add(cleaned))
            return cleaned;
        var n = 2;
        string next;
        do
        {
            next = cleaned + " (" + n + ")";
            n++;
        }
        while (!used.Add(next));
        return next;
    }

    static bool Inside(string parent, string child)
    {
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
