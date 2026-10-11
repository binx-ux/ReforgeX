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
    enum SortBy { Name, Size, Kind }

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
    readonly DiskWatch _disk = new();
    System.Windows.Threading.DispatcherTimer? _updateTimer;
    CancellationTokenSource? _update;
    string _query = "";
    string _status = "";
    string _place = "";
    string _pageKey = "";
    int _promo;
    bool _diskDirty;
    bool _updating;

    public MainWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _settings = SettingsStore.Load();
        _view = CollectionViewSource.GetDefaultView(_mods);
        _view.Filter = o => o is ModEntry mod && mod.Matches(_query) && FitsPlace(mod);
        ModList.ItemsSource = _view;
        _disk.Changed += OnDisk;
        ShowPromo();
    }

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _updateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(20)
        };
        _updateTimer.Tick += (_, _) => _ = CheckUpdate();
        _updateTimer.Start();
        await ScanAsync();
        await CheckUpdate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _work?.Cancel();
        _update?.Cancel();
        _updateTimer?.Stop();
        _disk.Stop();
        base.OnClosing(e);
    }

    void OnDisk()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_updating)
                return;
            if (_busy)
            {
                _diskDirty = true;
                return;
            }
            _ = ScanAsync(true);
        }));
    }

    void FilterBtn_Click(object sender, RoutedEventArgs e)
    {
        RebuildFilter();
        FilterMenu.PlacementTarget = FilterBtn;
        FilterMenu.IsOpen = true;
    }

    async void On_Click(object sender, RoutedEventArgs e)
    {
        await SetPower(true);
    }

    async void Off_Click(object sender, RoutedEventArgs e)
    {
        await SetPower(false);
    }

    void Ids_Click(object sender, RoutedEventArgs e)
    {
        CopyLaunch(_mods.Where(m => m.Selected).ToList());
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

    void KindSort_Click(object sender, RoutedEventArgs e)
    {
        if (_sort == SortBy.Kind)
            _asc = !_asc;
        else
        {
            _sort = SortBy.Kind;
            _asc = true;
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
            if (!mod.Selected)
                mod.Selected = true;
            _anchor = Shown().IndexOf(mod);
            OpenPage(mod);
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
            if (ModPage.Visibility == Visibility.Visible)
            {
                ClosePage();
                e.Handled = true;
                return;
            }
            _mute = true;
            foreach (var mod in _mods)
                mod.Selected = false;
            _mute = false;
            UpdateBar();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && ModPage.Visibility != Visibility.Visible)
        {
            var picked = _mods.Where(m => m.Selected).ToList();
            if (picked.Count == 1)
            {
                OpenPage(picked[0]);
                e.Handled = true;
            }
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

    void MenuView_Click(object sender, RoutedEventArgs e)
    {
        if (MenuMod(sender) is ModEntry mod)
            OpenPage(mod);
    }

    void MenuOpen_Click(object sender, RoutedEventArgs e)
    {
        if (MenuMod(sender) is ModEntry mod)
            OpenFolder(mod);
    }

    void RowMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;
        var mod = menu.DataContext as ModEntry;
        foreach (var item in menu.Items)
        {
            if (item is not MenuItem row || !Equals(row.Tag, "power"))
                continue;
            bool off = mod?.Disabled == true;
            row.Header = off ? "Turn on" : "Turn off";
            row.IsEnabled = mod != null && CanMove(mod);
        }
    }

    void MenuWorkshop_Click(object sender, RoutedEventArgs e)
    {
        var mod = MenuMod(sender);
        if (mod == null || mod.Guid.Length == 0)
        {
            _status = "No workshop id on that one.";
            UpdateBar();
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://reforger.armaplatform.com/workshop/" + mod.Guid,
            UseShellExecute = true
        });
    }

    void MenuLaunch_Click(object sender, RoutedEventArgs e)
    {
        var mod = MenuMod(sender);
        if (mod == null)
            return;
        CopyLaunch(new List<ModEntry> { mod });
    }

    void MenuPower_Click(object sender, RoutedEventArgs e)
    {
        var mod = MenuMod(sender);
        if (mod == null)
            return;
        FocusMod(mod);
        _ = SetPower(!mod.Disabled);
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

    async Task ScanAsync(bool quiet = false)
    {
        int id = ++_scanId;
        _diskDirty = false;
        _work?.Cancel();
        _work = new CancellationTokenSource();
        var token = _work.Token;
        if (!quiet)
        {
            _busy = true;
            _status = "";
            BusyText.Text = "Reading addon folders...";
            UpdateBar();
        }

        try
        {
            var progress = quiet ? null : new Progress<string>(text =>
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
            ModKinds.Apply(result.found);
            _roots = result.roots;
            _disk.Set(_roots.Where(r => r.Exists).Select(r => r.Path));
            var selected = new HashSet<string>(
                _mods.Where(m => m.Selected).Select(m => m.FolderPath),
                StringComparer.OrdinalIgnoreCase);
            _mute = true;
            _mods.Clear();
            foreach (var mod in result.found)
            {
                Watch(mod);
                if (selected.Contains(mod.FolderPath))
                    mod.Selected = true;
                _mods.Add(mod);
            }
            _mute = false;
            ApplySort();
            _busy = false;
            RefreshPage();
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
                        if (_pageKey.Length > 0 && SamePage(mod))
                            FillPage(mod);
                        UpdateBar();
                    });
                }
            }, token);
        }
        catch (OperationCanceledException)
        {
            if (id == _scanId && !quiet)
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
                if (_diskDirty && !_updating)
                {
                    _diskDirty = false;
                    _ = ScanAsync(true);
                }
            }
        }
    }

    async Task CheckUpdate()
    {
        if (_updating || !Updater.FromInstall())
            return;
        _update?.Cancel();
        _update = new CancellationTokenSource();
        var token = _update.Token;
        try
        {
            var release = await Updater.FindNewer(token);
            if (release == null || token.IsCancellationRequested)
                return;
            _updating = true;
            Subtitle.Text = "Downloading " + release.Version.ToString(3);
            var setup = await Updater.Download(release.Url, token);
            if (token.IsCancellationRequested)
                return;
            Subtitle.Text = "Installing " + release.Version.ToString(3);
            Updater.Apply(setup);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _updating = false;
            if (Subtitle.Text.StartsWith("Downloading", StringComparison.Ordinal) || Subtitle.Text.StartsWith("Installing", StringComparison.Ordinal))
                Subtitle.Text = "Arma Reforger mods";
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
        KindSort.Content = _sort == SortBy.Kind ? (_asc ? "Kind  ^" : "Kind  v") : "Kind";
        SizeSort.Content = _sort == SortBy.Size ? (_asc ? "Size  ^" : "Size  v") : "Size";
        _view.Refresh();
        UpdateBar();
    }

    int Compare(ModEntry a, ModEntry b)
    {
        int c;
        if (_sort == SortBy.Size)
            c = a.Bytes.CompareTo(b.Bytes);
        else if (_sort == SortBy.Kind)
            c = ModKinds.Rank(a.Kind).CompareTo(ModKinds.Rank(b.Kind));
        else
            c = string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
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
        OnBtn.IsEnabled = !_busy && selected.Any(m => m.Disabled && CanMove(m));
        OffBtn.IsEnabled = !_busy && selected.Any(m => !m.Disabled && CanMove(m));
        IdsBtn.IsEnabled = !_busy && selected.Any(m => m.Guid.Length > 0);
        FilterBtn.IsEnabled = !_busy;
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
        var list = _roots.Where(r => !r.Locked && !r.HoldingDisabled).ToList();
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

    bool FitsPlace(ModEntry mod)
    {
        if (_place.Length == 0)
            return true;
        if (_place == "on")
            return !mod.Disabled;
        if (_place == "off")
            return mod.Disabled;
        if (_place.StartsWith("kind:", StringComparison.Ordinal))
            return string.Equals(mod.Kind, _place.Substring(5), StringComparison.OrdinalIgnoreCase);
        return string.Equals(mod.Source, _place, StringComparison.OrdinalIgnoreCase);
    }

    void RebuildFilter()
    {
        FilterMenu.Items.Clear();
        FilterMenu.Items.Add(PlaceItem("All mods", ""));
        FilterMenu.Items.Add(PlaceItem("On", "on"));
        FilterMenu.Items.Add(PlaceItem("Off", "off"));
        FilterMenu.Items.Add(new Separator());
        FilterMenu.Items.Add(PlaceItem("Core", "kind:" + ModKinds.Core));
        FilterMenu.Items.Add(PlaceItem("Framework", "kind:" + ModKinds.Framework));
        FilterMenu.Items.Add(PlaceItem("Addons", "kind:" + ModKinds.Addon));
        var sources = _mods.Select(m => m.Source).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToList();
        if (sources.Count == 0)
            return;
        FilterMenu.Items.Add(new Separator());
        foreach (var source in sources)
            FilterMenu.Items.Add(PlaceItem(source, source));
    }

    MenuItem PlaceItem(string header, string place)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = string.Equals(_place, place, StringComparison.Ordinal) };
        item.Click += (_, _) =>
        {
            _place = place;
            FilterBtn.Content = header;
            _view.Refresh();
            UpdateBar();
        };
        return item;
    }

    static bool CanMove(ModEntry mod)
    {
        if (mod.Locked)
            return false;
        if (string.Equals(mod.Source, "Workshop", StringComparison.OrdinalIgnoreCase))
            return false;
        return RecycleBin.Safe(mod.FolderPath);
    }

    async Task SetPower(bool enabled)
    {
        if (_busy)
            return;
        var picked = _mods.Where(m => m.Selected && CanMove(m) && m.Disabled == enabled).ToList();
        if (picked.Count == 0)
        {
            _status = "Select mods you can turn on or off. Workshop and game files stay put.";
            UpdateBar();
            return;
        }

        if (!enabled)
        {
            var word = picked.Count == 1 ? "mod" : "mods";
            var names = picked.Count <= 3
                ? string.Join(", ", picked.Select(m => m.Title))
                : string.Join(", ", picked.Take(2).Select(m => m.Title)) + ", and " + (picked.Count - 2) + " more";
            var dlg = new ConfirmWindow("Turn off " + picked.Count + " " + word + "?", names, "They move into a folder next to the addon folder. The game will not load them.", "Turn off", false)
            {
                Owner = this
            };
            if (dlg.ShowDialog() != true)
                return;
        }

        _busy = true;
        _status = enabled ? "Turning on..." : "Turning off...";
        UpdateBar();
        var failed = new List<string>();
        int moved = 0;
        await Task.Run(() =>
        {
            foreach (var mod in picked)
            {
                try
                {
                    AddonLibrary.SetEnabled(mod, enabled);
                    moved++;
                }
                catch (Exception ex)
                {
                    failed.Add(mod.Title + " (" + ex.Message + ")");
                }
            }
        });

        if (failed.Count == 0)
            _status = (enabled ? "Turned on " : "Turned off ") + moved + ".";
        else
            _status = "Could not move " + string.Join(", ", failed) + ".";
        _busy = false;
        var note = _status;
        await ScanAsync();
        _status = note;
        UpdateBar();
    }

    void CopyLaunch(List<ModEntry> mods)
    {
        var ids = mods.Select(m => m.Guid).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
        {
            _status = "Those mods have no id to copy.";
            UpdateBar();
            return;
        }
        var line = "-addons " + string.Join(",", ids);
        Clipboard.SetText(line);
        _status = "Copied " + line;
        UpdateBar();
    }

    void OpenPage(ModEntry mod)
    {
        _pageKey = mod.Guid.Length > 0 ? mod.Guid : mod.FolderPath;
        FillPage(mod);
        ModPage.Visibility = Visibility.Visible;
    }

    void ClosePage()
    {
        _pageKey = "";
        ModPage.Visibility = Visibility.Collapsed;
    }

    void RefreshPage()
    {
        if (_pageKey.Length == 0)
            return;
        var mod = _mods.FirstOrDefault(SamePage);
        if (mod == null)
        {
            ClosePage();
            return;
        }
        FillPage(mod);
    }

    bool SamePage(ModEntry mod)
    {
        if (mod.Guid.Length > 0 && string.Equals(mod.Guid, _pageKey, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(mod.FolderPath, _pageKey, StringComparison.OrdinalIgnoreCase);
    }

    void FillPage(ModEntry mod)
    {
        PageTitle.Text = mod.Title;
        PageKind.Text = mod.Kind;
        if (mod.Kind == ModKinds.Core)
        {
            PageKind.Foreground = (System.Windows.Media.Brush)FindResource("CoreInk");
        }
        else if (mod.Kind == ModKinds.Framework)
        {
            PageKind.Foreground = (System.Windows.Media.Brush)FindResource("FrameInk");
        }
        else
        {
            PageKind.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
        }
        var meta = new List<string>();
        if (mod.Version.Length > 0)
            meta.Add(mod.Version);
        meta.Add(mod.SizeText);
        if (mod.Source.Length > 0)
            meta.Add(mod.Source);
        meta.Add(mod.Disabled ? "Off" : "On");
        if (mod.Locked)
            meta.Add("With the game");
        PageMeta.Text = string.Join("  ·  ", meta);

        var id = mod.Guid.Length > 0 ? mod.Guid : mod.FolderName;
        if (mod.ProjectId.Length > 0 && !string.Equals(mod.ProjectId, id, StringComparison.OrdinalIgnoreCase))
            id += "   " + mod.ProjectId;
        PageId.Text = id;
        PageFolder.Text = mod.FolderPath;
        var needs = NeedLines(mod);
        PageNeeds.ItemsSource = needs;
        PageNeeds.Visibility = needs.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageNeedsEmpty.Visibility = needs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var used = UsedLines(mod);
        PageUsed.ItemsSource = used;
        PageUsed.Visibility = used.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageUsedEmpty.Visibility = used.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PagePowerBtn.Content = mod.Disabled ? "Turn on" : "Turn off";
        PagePowerBtn.IsEnabled = CanMove(mod);
    }

    List<string> NeedLines(ModEntry mod)
    {
        var lines = new List<string>();
        foreach (var guid in mod.Needs)
        {
            var hit = _mods.FirstOrDefault(m => string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase));
            lines.Add(hit == null ? guid : hit.Title);
        }
        lines.Sort(StringComparer.CurrentCultureIgnoreCase);
        return lines;
    }

    List<string> UsedLines(ModEntry mod)
    {
        if (mod.Guid.Length == 0)
            return new List<string>();
        return _mods
            .Where(m => m != mod && m.Needs.Any(g => string.Equals(g, mod.Guid, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Title)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    void ShowPromo()
    {
        var cards = PromoCards.Visible(_settings.HiddenPromos);
        if (cards.Count == 0)
        {
            PromoRow.Visibility = Visibility.Collapsed;
            return;
        }
        if (_promo < 0 || _promo >= cards.Count)
            _promo = 0;
        var card = cards[_promo];
        PromoKicker.Text = card.Kicker;
        PromoTitle.Text = card.Title;
        PromoLine.Text = card.Line;
        PromoGo.Content = card.Button;
        PromoGo.Tag = card.Url;
        PromoNext.Tag = card.Id;
        PromoNext.Visibility = cards.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PromoRow.Visibility = Visibility.Visible;
    }

    void PromoGo_Click(object sender, RoutedEventArgs e)
    {
        if (PromoGo.Tag is not string url || url.Length == 0)
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    void PromoNext_Click(object sender, RoutedEventArgs e)
    {
        var cards = PromoCards.Visible(_settings.HiddenPromos);
        if (cards.Count == 0)
            return;
        _promo = (_promo + 1) % cards.Count;
        ShowPromo();
    }

    void PromoHide_Click(object sender, RoutedEventArgs e)
    {
        if (PromoNext.Tag is string id && id.Length > 0 && !_settings.HiddenPromos.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            _settings.HiddenPromos.Add(id);
            _settings.Save();
        }
        _promo = 0;
        ShowPromo();
    }

    void PageBack_Click(object sender, RoutedEventArgs e)
    {
        ClosePage();
    }

    void PageFolder_Click(object sender, RoutedEventArgs e)
    {
        var mod = _mods.FirstOrDefault(SamePage);
        if (mod != null)
            OpenFolder(mod);
    }

    void PageWorkshop_Click(object sender, RoutedEventArgs e)
    {
        var mod = _mods.FirstOrDefault(SamePage);
        if (mod == null || mod.Guid.Length == 0)
        {
            _status = "No workshop id on that one.";
            UpdateBar();
            return;
        }
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://reforger.armaplatform.com/workshop/" + mod.Guid,
            UseShellExecute = true
        });
    }

    void PagePower_Click(object sender, RoutedEventArgs e)
    {
        var mod = _mods.FirstOrDefault(SamePage);
        if (mod == null)
            return;
        _mute = true;
        foreach (var item in _mods)
            item.Selected = false;
        mod.Selected = true;
        _mute = false;
        UpdateBar();
        _ = SetPower(!mod.Disabled);
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
