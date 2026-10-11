using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ReforgeX;

public partial class FoldersWindow : Window
{
    readonly List<string> _extra;

    public List<string> ExtraRoots => _extra;

    public FoldersWindow(IEnumerable<string> extra)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _extra = extra.ToList();
        Rebuild();
    }

    void Rebuild()
    {
        Roots.ItemsSource = null;
        Roots.ItemsSource = AddonLibrary.Discover(_extra).Where(r => !r.HoldingDisabled).ToList();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Folder that contains addon folders"
        };
        if (dlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dlg.FolderName))
            return;
        _extra.Add(dlg.FolderName);
        Rebuild();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not ScanRoot root)
            return;
        _extra.RemoveAll(p =>
        {
            try
            {
                return string.Equals(Path.GetFullPath(p), root.Path, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
        Rebuild();
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not ScanRoot root)
            return;
        if (!Directory.Exists(root.Path))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + root.Path + "\"",
            UseShellExecute = true
        });
    }

    void Done_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
