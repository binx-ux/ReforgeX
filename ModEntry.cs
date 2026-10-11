using System.ComponentModel;

namespace ReforgeX;

sealed class ModEntry : INotifyPropertyChanged
{
    public string Title { get; init; } = "";
    public string Guid { get; init; } = "";
    public string ProjectId { get; init; } = "";
    public string Version { get; init; } = "";
    public string FolderName { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public string Source { get; init; } = "";
    public string Kind { get; set; } = "Addon";
    public List<string> Needs { get; init; } = new();
    long _bytes;
    bool _sized;

    public long Bytes
    {
        get => _bytes;
        set
        {
            if (_bytes == value)
                return;
            _bytes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        }
    }

    public bool Sized
    {
        get => _sized;
        set
        {
            if (_sized == value)
                return;
            _sized = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sized)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        }
    }

    public bool Locked { get; init; }
    public bool Disabled { get; init; }

    bool _selected;

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
                return;
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public string SizeText => _sized ? AddonLibrary.FormatSize(_bytes) : "...";

    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (Guid.Length > 0 && !string.Equals(Guid, Title, StringComparison.OrdinalIgnoreCase))
                parts.Add(Guid);
            else if (FolderName.Length > 0 && !string.Equals(FolderName, Title, StringComparison.OrdinalIgnoreCase))
                parts.Add(FolderName);
            if (Version.Length > 0)
                parts.Add(Version);
            if (Source.Length > 0)
                parts.Add(Source);
            if (Disabled)
                parts.Add("Off");
            if (Locked)
                parts.Add("With the game");
            return string.Join("  ·  ", parts);
        }
    }

    public bool Matches(string query)
    {
        if (query.Length == 0)
            return true;
        return Contains(Title, query)
            || Contains(Guid, query)
            || Contains(ProjectId, query)
            || Contains(FolderName, query)
            || Contains(Source, query)
            || Contains(Kind, query)
            || Contains(Version, query);
    }

    static bool Contains(string value, string query)
    {
        return value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

sealed class ScanRoot
{
    public string Path { get; init; } = "";
    public string Label { get; init; } = "";
    public bool Locked { get; init; }
    public bool UserAdded { get; init; }
    public bool Exists { get; init; }
    public bool HoldingDisabled { get; init; }
    public string Status => Exists ? "Found" : "Not found";
}
