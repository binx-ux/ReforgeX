using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ReforgeX;

public partial class MainWindow : Window
{
    enum SortBy { Name, Size }

    readonly ObservableCollection<ModEntry> _mods = new();
    readonly ICollectionView _view;
    SettingsStore _settings = new();
    List<ScanRoot> _roots = new();
    CancellationTokenSource? _work;
    SortBy _sort = SortBy.Name;
    bool _asc = true;
    bool _busy;
    bool _mute;
    int _anchor = -1;
    int _scanId;
    string _query = "";
    string _status = "";

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _settings = SettingsStore.Load();
        _view = CollectionViewSource.GetDefaultView(_mods);
        _view.Filter = o => o is ModEntry mod && mod.Matches(_query);
        ModList.ItemsSource = _view;
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _work?.Cancel();
        base.OnClosing(e);
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _status = "";
        await ScanAsync();
    }

    void Folders_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var dlg = new FoldersWindow(_settings.ExtraRoots) { Owner = this };
        if (dlg.ShowDialog() != true)
            return;
        _settings.ExtraRoots = dlg.ExtraRoots;
        _settings.Save();
        _ = ScanAsync();
    }

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var pick = new ConfirmWindow("Import mods", "Use a zip from Share, or point at an addon folder.", "", "Choose zip", false)
        {
            Owner = this
        };
        pick.ShowExtra("Choose folder");
        if (pick.ShowDialog() != true)
            return;
        if (pick.Result == ConfirmWindow.Answer.Extra)
        {
            var folderDlg = new OpenFolderDialog { Title = "Addon folder" };
            if (folderDlg.ShowDialog(this) != true || string.IsNullOrWhiteSpace(folderDlg.FolderName))
                return;
            await ImportFolder(folderDlg.FolderName);
            return;
        }
        var dlg = new OpenFileDialog
        {
            Title = "Import mods",
            Filter = "Zip (*.zip)|*.zip|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true)
            return;
        await ImportZip(dlg.FileName);
    }

    async void Share_Click(object sender, RoutedEventArgs e)
    {
        await ShareSelected();
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        await RemoveSelected();
    }

    void Show_Click(object sender, RoutedEventArgs e)
    {
        var picked = _mods.Where(m => m.Selected).ToList();
        if (picked.Count != 1)
            return;
        OpenFolder(picked[0]);
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _work?.Cancel();
    }

    void NameSort_Click(object sender, RoutedEventArgs e)
    {
        if (_sort == SortBy.Name)
            _asc = !_asc;
        else
        {
            _sort = SortBy.Name;
            _asc = true;
        }
        ApplySort();
    }

    void SizeSort_Click(object sender, RoutedEventArgs e)
    {
        if (_sort == SortBy.Size)
            _asc = !_asc;
        else
        {
            _sort = SortBy.Size;
            _asc = false;
        }
        ApplySort();
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        _query = SearchBox.Text.Trim();
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh();
        UpdateBar();
    }

    void SelectAll_Down(object sender, MouseButtonEventArgs e)
    {
        var shown = Shown();
        bool all = shown.Count > 0 && shown.All(m => m.Selected);
        _mute = true;
        foreach (var mod in shown)
            mod.Selected = !all;
        _mute = false;
        if (shown.Count > 0)
            _anchor = 0;
        UpdateBar();
        e.Handled = true;
    }

    void Check_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not ModEntry mod)
            return;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            SelectRange(mod);
            e.Handled = true;
            return;
        }
        _anchor = Shown().IndexOf(mod);
    }

    void Row_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject src && FindAncestor<CheckBox>(src) != null)
            return;
        if (sender is not FrameworkElement el || el.DataContext is not ModEntry mod)
            return;
        if (e.ClickCount == 2)
        {
            mod.Selected = !mod.Selected;
            OpenFolder(mod);
            e.Handled = true;
            return;
        }
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            SelectRange(mod);
        else
        {
            mod.Selected = !mod.Selected;
            _anchor = Shown().IndexOf(mod);
        }
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SearchBox.IsKeyboardFocused)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _mute = true;
            foreach (var mod in Shown())
                mod.Selected = true;
            _mute = false;
            UpdateBar();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            _mute = true;
            foreach (var mod in _mods)
                mod.Selected = false;
            _mute = false;
            UpdateBar();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete)
        {
            _ = RemoveSelected();
            e.Handled = true;
        }
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_busy)
            return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;
        foreach (var file in files)
        {
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                await ImportZip(file);
            else if (Directory.Exists(file))
                await ImportFolder(file);
        }
    }

    void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (MenuMod(sender) is ModEntry mod)
            OpenFolder(mod);
    }

    void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        var mod = MenuMod(sender);
        if (mod == null)
            return;
        var text = mod.Guid.Length > 0 ? mod.Guid : (mod.ProjectId.Length > 0 ? mod.ProjectId : mod.FolderName);
        if (text.Length == 0)
            return;
        Clipboard.SetText(text);
        _status = "Copied " + text;
        UpdateBar();
    }

    void MenuShare_Click(object sender, RoutedEventArgs e)
    {
        FocusMod(MenuMod(sender));
        _ = ShareSelected();
    }

    void MenuRemove_Click(object sender, RoutedEventArgs e)
    {
        FocusMod(MenuMod(sender));
        _ = RemoveSelected();
    }

    async Task ScanAsync()
    {
        int id = ++_scanId;
        _work?.Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        _busy = true;
        _status = "";
        BusyText.Text = "Reading addon folders...";
        UpdateBar();

        try
        {
            var progress = new Progress<string>(text =>
            {
                if (id != _scanId)
                    return;
                BusyText.Text = text;
                if (_mods.Count > 0)
                    CountText.Text = text;
            });
            var extra = _settings.ExtraRoots.ToList();
            var result = await Task.Run(() =>
            {
                var roots = AddonLibrary.Discover(extra);
                var found = AddonLibrary.Scan(roots, progress, token);
                return (roots, found);
            }, token);
            if (id != _scanId)
                return;
            token.ThrowIfCancellationRequested();
            _roots = result.roots;
            _mute = true;
            _mods.Clear();
            foreach (var mod in result.found)
            {
                Watch(mod);
                _mods.Add(mod);
            }
            _mute = false;
            ApplySort();
            _busy = false;
            UpdateBar();
            var sizing = result.found;
            await Task.Run(() =>
            {
                foreach (var mod in sizing)
                {
                    token.ThrowIfCancellationRequested();
                    long bytes = AddonLibrary.Measure(mod.FolderPath);
                    Dispatcher.Invoke(() =>
                    {
                        if (id != _scanId)
                            return;
                        mod.Bytes = bytes;
                        mod.Sized = true;
                        UpdateBar();
                    });
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            if (id == _scanId)
                _status = "Canceled.";
        }
        catch (Exception ex)
        {
            if (id == _scanId)
                _status = ex.Message;
        }
        finally
        {
            if (id == _scanId)
            {
                _busy = false;
                UpdateBar();
            }
        }
    }

    async Task RemoveSelected()
    {
        if (_busy)
            return;
        var picked = _mods.Where(m => m.Selected && !m.Locked && RecycleBin.Safe(m.FolderPath) && !IsProtectedRoot(m)).ToList();
        int locked = _mods.Count(m => m.Selected && (m.Locked || IsProtectedRoot(m)));
        if (picked.Count == 0)
        {
            _status = locked > 0 ? "Those stay with the game install." : "Select mods first.";
            UpdateBar();
            return;
        }

        string names;
        if (picked.Count <= 3)
            names = string.Join(", ", picked.Select(m => m.Title));
        else
            names = string.Join(", ", picked.Take(2).Select(m => m.Title)) + ", and " + (picked.Count - 2) + " more";

        var detail = AddonLibrary.FormatSize(picked.Sum(m => (long)m.Bytes)) + " goes to the Recycle Bin.";
        if (locked > 0)
            detail += " " + locked + (locked == 1 ? " game mod stays." : " game mods stay.");

        var word = picked.Count == 1 ? "mod" : "mods";
        var dlg = new ConfirmWindow("Remove " + picked.Count + " " + word + "?", names, detail, "Remove", true)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true)
            return;

        _work?.Cancel();
        _work = new CancellationTokenSource();
        _busy = true;
        _status = "Removing...";
        UpdateBar();

        var failed = new List<string>();
        int gone = 0;
        await Task.Run(() =>
        {
            foreach (var mod in picked)
            {
                try
                {
                    RecycleBin.Delete(mod.FolderPath);
                    gone++;
                }
                catch
                {
                    failed.Add(mod.Title);
                }
            }
        });

        if (failed.Count == 0)
            _status = "Removed " + gone + ".";
        else
            _status = "Could not remove " + string.Join(", ", failed) + ".";
        _busy = false;
        await ScanAsync();
        if (string.IsNullOrEmpty(_status) || _status == "Canceled.")
            _status = failed.Count == 0 ? "Removed " + gone + "." : "Could not remove " + string.Join(", ", failed) + ".";
        UpdateBar();
    }

    async Task ShareSelected()
    {
        if (_busy)
            return;
        var picked = _mods.Where(m => m.Selected).ToList();
        if (picked.Count == 0)
        {
            _status = "Select mods first.";
            UpdateBar();
            return;
        }

        var name = picked.Count == 1 ? Packer.SafeFolder(picked[0].Title) + ".zip" : "ReforgeX mods.zip";
        var dlg = new SaveFileDialog
        {
            Title = "Share mods",
            Filter = "Zip (*.zip)|*.zip",
            FileName = name,
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dlg.ShowDialog(this) != true)
            return;

        _work?.Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        _busy = true;
        _status = "Packing...";
        UpdateBar();
        try
        {
            var progress = new Progress<string>(text =>
            {
                _status = text;
                UpdateBar();
            });
            await Task.Run(() => Packer.Export(picked, dlg.FileName, progress, token), token);
            var len = new FileInfo(dlg.FileName).Length;
            _status = "Saved " + Path.GetFileName(dlg.FileName) + " (" + AddonLibrary.FormatSize(len) + ")";
        }
        catch (OperationCanceledException)
        {
            _status = "Share canceled.";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        finally
        {
            _busy = false;
            UpdateBar();
        }
    }

    async Task ImportZip(string zipPath)
    {
        List<Incoming> items;
        try
        {
            items = await Task.Run(() => Packer.InspectZip(zipPath));
        }
        catch (InvalidDataException)
        {
            _status = "That file is not a zip.";
            UpdateBar();
            return;
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            UpdateBar();
            return;
        }
        await ImportItems(items, zipPath);
    }

    async Task ImportFolder(string path)
    {
        var items = Packer.InspectFolder(path);
        await ImportItems(items, null);
    }

    async Task ImportItems(List<Incoming> items, string? zipPath)
    {
        if (items.Count == 0)
        {
            _status = "No addon files in there.";
            UpdateBar();
            return;
        }

        var writable = WritableRoots();
        var current = _settings.ImportRoot;
        var word = items.Count == 1 ? "mod" : "mods";
        var names = items.Count <= 3
            ? string.Join(", ", items.Select(i => i.Title))
            : string.Join(", ", items.Take(2).Select(i => i.Title)) + ", and " + (items.Count - 2) + " more";

        var destGuess = DestPath(writable, current);
        var clashes = items.Where(i => Directory.Exists(Path.Combine(destGuess, i.Folder))).ToList();
        var detail = clashes.Count == 0
            ? "The addon folders are copied in as the game stores them."
            : clashes.Count + (clashes.Count == 1 ? " is already in that folder." : " are already in that folder.");

        var dlg = new ConfirmWindow("Import " + items.Count + " " + word + "?", names, detail, clashes.Count > 0 ? "Replace" : "Import", false)
        {
            Owner = this
        };
        dlg.OfferFolders(writable, current);
        if (clashes.Count > 0)
            dlg.ShowExtra("Skip existing");
        if (dlg.ShowDialog() != true || dlg.Result == ConfirmWindow.Answer.Cancel)
            return;

        var dest = dlg.ChosenFolder(writable);
        if (string.IsNullOrWhiteSpace(dest))
        {
            _status = "Pick a folder first.";
            UpdateBar();
            return;
        }

        bool replace = dlg.Result == ConfirmWindow.Answer.Accept && clashes.Count > 0;
        if (dlg.Result == ConfirmWindow.Answer.Extra)
        {
            items = items.Where(i => !Directory.Exists(Path.Combine(dest, i.Folder))).ToList();
            replace = false;
            if (items.Count == 0)
            {
                _status = "Those mods are already there.";
                UpdateBar();
                return;
            }
        }
        else if (clashes.Count == 0)
        {
            replace = false;
        }

        _settings.ImportRoot = dest;
        _settings.Save();

        _work?.Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        _busy = true;
        _status = "Importing...";
        UpdateBar();
        try
        {
            var batch = items;
            var progress = new Progress<string>(text =>
            {
                _status = text;
                UpdateBar();
            });
            int wrote = await Task.Run(() => Packer.Import(zipPath, batch, dest, replace, progress, token), token);
            _status = wrote == 0 ? "Nothing new to import." : "Imported " + wrote + ".";
        }
        catch (OperationCanceledException)
        {
            _status = "Import canceled.";
        }
        catch (Exception ex)
        {
            _status = ex.Message;
        }
        finally
        {
            _busy = false;
        }

        var note = _status;
        await ScanAsync();
        _status = note;
        UpdateBar();
    }

    void ApplySort()
    {
        var items = _mods.ToList();
        items.Sort(Compare);
        _mute = true;
        _mods.Clear();
        foreach (var mod in items)
            _mods.Add(mod);
        _mute = false;
        NameSort.Content = _sort == SortBy.Name ? (_asc ? "Name  ^" : "Name  v") : "Name";
        SizeSort.Content = _sort == SortBy.Size ? (_asc ? "Size  ^" : "Size  v") : "Size";
        _view.Refresh();
        UpdateBar();
    }

    int Compare(ModEntry a, ModEntry b)
    {
        int c = _sort == SortBy.Size
            ? a.Bytes.CompareTo(b.Bytes)
            : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
        if (!_asc)
            c = -c;
        if (c != 0)
            return c;
        return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
    }

    void UpdateBar()
    {
        var shown = Shown();
        var selected = _mods.Where(m => m.Selected).ToList();
        long totalBytes = 0;
        foreach (var mod in _mods)
            totalBytes += mod.Bytes;

        string countLine;
        if (_busy && _mods.Count == 0)
            countLine = "Reading addon folders...";
        else if (shown.Count != _mods.Count)
            countLine = shown.Count + " shown  ·  " + _mods.Count + " mods  ·  " + AddonLibrary.FormatSize(totalBytes);
        else
            countLine = _mods.Count + (_mods.Count == 1 ? " mod" : " mods") + "  ·  " + AddonLibrary.FormatSize(totalBytes);
        if (_mods.Any(m => !m.Sized))
            countLine += "  ·  sizing";
        CountText.Text = countLine;

        if (_status.Length > 0)
        {
            SelectionText.Text = _status;
            SelectionText.Foreground = _status.StartsWith("Could not", StringComparison.Ordinal)
                || _status.StartsWith("Refusing", StringComparison.Ordinal)
                ? (Brush)FindResource("Danger")
                : (Brush)FindResource("Muted");
        }
        else if (selected.Count == 0)
        {
            SelectionText.Text = "Select mods to remove or share";
            SelectionText.Foreground = (Brush)FindResource("Muted");
        }
        else
        {
            long pickedBytes = 0;
            foreach (var mod in selected)
                pickedBytes += mod.Bytes;
            SelectionText.Text = selected.Count + " selected  ·  " + AddonLibrary.FormatSize(pickedBytes);
            SelectionText.Foreground = (Brush)FindResource("Text");
        }

        int shownSelected = shown.Count(m => m.Selected);
        if (shown.Count == 0 || shownSelected == 0)
            SelectAllBox.IsChecked = false;
        else if (shownSelected == shown.Count)
            SelectAllBox.IsChecked = true;
        else
            SelectAllBox.IsChecked = null;

        RemoveBtn.IsEnabled = !_busy && selected.Any(m => !m.Locked);
        ShareBtn.IsEnabled = !_busy && selected.Count > 0;
        ShowBtn.IsEnabled = !_busy && selected.Count == 1;
        ImportBtn.IsEnabled = !_busy;
        RefreshBtn.IsEnabled = !_busy;
        FoldersBtn.IsEnabled = !_busy;
        CancelBtn.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        ColumnHead.Visibility = _mods.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyPane.Visibility = !_busy && _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Visibility = _busy && _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatchText.Visibility = !_busy && _mods.Count > 0 && shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModList.Visibility = _mods.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    void Watch(ModEntry mod)
    {
        mod.PropertyChanged += (_, args) =>
        {
            if (_mute)
                return;
            if (args.PropertyName == nameof(ModEntry.Selected))
                UpdateBar();
        };
    }

    void SelectRange(ModEntry mod)
    {
        var shown = Shown();
        int index = shown.IndexOf(mod);
        if (index < 0)
            return;
        if (_anchor < 0 || _anchor >= shown.Count)
            _anchor = index;
        int a = Math.Min(_anchor, index);
        int b = Math.Max(_anchor, index);
        _mute = true;
        for (int i = 0; i < shown.Count; i++)
            shown[i].Selected = i >= a && i <= b;
        _mute = false;
        UpdateBar();
    }

    List<ModEntry> Shown()
    {
        return _view.Cast<ModEntry>().ToList();
    }

    List<ScanRoot> WritableRoots()
    {
        var list = _roots.Where(r => !r.Locked).ToList();
        if (list.Count == 0)
        {
            var path = AddonLibrary.DefaultAddonsPath();
            list.Add(new ScanRoot
            {
                Path = path,
                Label = "Documents",
                Exists = Directory.Exists(path)
            });
        }
        return list;
    }

    static string DestPath(IReadOnlyList<ScanRoot> roots, string? current)
    {
        var hit = roots.FirstOrDefault(r => string.Equals(r.Path, current, StringComparison.OrdinalIgnoreCase));
        if (hit != null)
            return hit.Path;
        return roots.Count > 0 ? roots[0].Path : AddonLibrary.DefaultAddonsPath();
    }

    bool IsProtectedRoot(ModEntry mod)
    {
        string full;
        try
        {
            full = Path.GetFullPath(mod.FolderPath);
        }
        catch
        {
            return true;
        }
        foreach (var root in _roots)
        {
            if (root.UserAdded)
                continue;
            try
            {
                if (string.Equals(Path.GetFullPath(root.Path), full, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }
        }
        return false;
    }

    void FocusMod(ModEntry? mod)
    {
        if (mod == null)
            return;
        if (mod.Selected)
            return;
        _mute = true;
        foreach (var item in _mods)
            item.Selected = false;
        mod.Selected = true;
        _mute = false;
        UpdateBar();
    }

    static ModEntry? MenuMod(object sender)
    {
        return sender is FrameworkElement el ? el.DataContext as ModEntry : null;
    }

    static void OpenFolder(ModEntry mod)
    {
        if (!Directory.Exists(mod.FolderPath))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + mod.FolderPath + "\"",
            UseShellExecute = true
        });
    }

    static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        while (start != null)
        {
            if (start is T hit)
                return hit;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }
}
