namespace ReforgeX;

static class ModKinds
{
    public const string Core = "Core";
    public const string Framework = "Framework";
    public const string Addon = "Addon";

    static readonly HashSet<string> FrameworkGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "5E389BB9F58B79A6",
        "5D6EA74A94173EDF",
        "5D6EBC81EB1842EF",
        "606B100247F5C709"
    };

    static readonly HashSet<string> CoreGuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "64610AFB74AA9842",
        "595F2BF2F44836FB",
        "5CF4A61BE1D960C6"
    };

    public static void Apply(IReadOnlyList<ModEntry> mods)
    {
        foreach (var mod in mods)
            mod.Kind = Classify(mod);
    }

    public static int Rank(string kind)
    {
        if (kind == Core)
            return 0;
        if (kind == Framework)
            return 1;
        return 2;
    }

    static string Classify(ModEntry mod)
    {
        if (FrameworkGuids.Contains(mod.Guid) || LooksLikeFramework(mod.Title))
            return Framework;
        if (CoreGuids.Contains(mod.Guid) || LooksLikeCore(mod.Title))
            return Core;
        return Addon;
    }

    static bool LooksLikeFramework(string title)
    {
        if (title.Contains("framework", StringComparison.OrdinalIgnoreCase))
            return true;
        if (title.Contains("spacecore", StringComparison.OrdinalIgnoreCase))
            return true;
        return title.Contains("space core", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeCore(string title)
    {
        if (HasToken(title, "core"))
            return true;
        if (title.EndsWith("Core", StringComparison.Ordinal))
            return true;
        var trimmed = title.Trim();
        if (trimmed.Equals("RHS: Status Quo", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("RHS - Status Quo", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.Equals("RHS: Status Quo Dev", StringComparison.OrdinalIgnoreCase))
            return true;
        return trimmed.Equals("RHS - Status Quo Dev", StringComparison.OrdinalIgnoreCase);
    }

    static bool HasToken(string title, string word)
    {
        int i = 0;
        while ((i = title.IndexOf(word, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool left = i == 0 || !char.IsLetter(title[i - 1]);
            int end = i + word.Length;
            bool right = end >= title.Length || !char.IsLetter(title[end]);
            if (left && right)
                return true;
            i = end;
        }
        return false;
    }
}
