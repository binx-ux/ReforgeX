namespace ReforgeX;

sealed class DiskWatch : IDisposable
{
    readonly object _gate = new();
    readonly List<FileSystemWatcher> _watchers = new();
    Timer? _timer;

    public event Action? Changed;

    public void Set(IEnumerable<string> roots)
    {
        Drop(Swap(roots));
    }

    public void Stop()
    {
        Drop(Swap(null));
    }

    public void Dispose()
    {
        Stop();
    }

    void Hit(object sender, FileSystemEventArgs e)
    {
        Arm();
    }

    void HitError(object sender, ErrorEventArgs e)
    {
        Arm();
    }

    void Arm()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = new Timer(_ => Changed?.Invoke(), null, 1200, Timeout.Infinite);
        }
    }

    List<FileSystemWatcher> Swap(IEnumerable<string>? roots)
    {
        lock (_gate)
        {
            var old = _watchers.ToList();
            _watchers.Clear();
            _timer?.Dispose();
            _timer = null;
            if (roots == null)
                return old;
            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root))
                    continue;
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 65536
                };
                watcher.Created += Hit;
                watcher.Deleted += Hit;
                watcher.Changed += Hit;
                watcher.Renamed += Hit;
                watcher.Error += HitError;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            return old;
        }
    }

    static void Drop(List<FileSystemWatcher> watchers)
    {
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }
}
