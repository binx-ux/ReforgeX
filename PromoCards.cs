namespace ReforgeX;

sealed class PromoCard
{
    public string Id { get; init; } = "";
    public string Kicker { get; init; } = "";
    public string Title { get; init; } = "";
    public string Line { get; init; } = "";
    public string Button { get; init; } = "";
    public string Url { get; init; } = "";
}

static class PromoCards
{
    static readonly PromoCard[] All =
    {
        new()
        {
            Id = "onedrive",
            Kicker = "Cloud storage",
            Title = "Keep a copy off this PC",
            Line = "Addon folders get big. OneDrive can hold a backup of the packs you share.",
            Button = "Open OneDrive",
            Url = "https://www.microsoft.com/microsoft-365/onedrive/online-cloud-storage"
        },
        new()
        {
            Id = "drive",
            Kicker = "Cloud storage",
            Title = "Park the heavy mods",
            Line = "Google Drive is useful when a pack is too large to keep only on this drive.",
            Button = "Open Drive",
            Url = "https://www.google.com/drive/"
        },
        new()
        {
            Id = "dropbox",
            Kicker = "Cloud storage",
            Title = "Send a pack from another PC",
            Line = "Dropbox is an easy place to leave a shared zip for someone who is not on this machine.",
            Button = "Open Dropbox",
            Url = "https://www.dropbox.com/"
        }
    };

    public static List<PromoCard> Visible(IReadOnlyList<string>? hidden)
    {
        var skip = hidden ?? new List<string>();
        return All.Where(card => !skip.Contains(card.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}
