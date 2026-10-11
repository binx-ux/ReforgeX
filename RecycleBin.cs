using System.Runtime.InteropServices;

namespace ReforgeX;

static class RecycleBin
{
    public static void Delete(string path)
    {
        var full = Path.GetFullPath(path);
        if (!Safe(full))
            throw new IOException("Refusing to delete that folder.");
        if (!Directory.Exists(full))
            return;

        var from = Marshal.StringToHGlobalUni(full + "\0");
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = 3,
                pFrom = from,
                fFlags = 0x0010 | 0x0040 | 0x0400 | 0x0004
            };
            int code = SHFileOperation(ref op);
            if (code != 0 || op.fAnyOperationsAborted != 0)
                throw new IOException("Could not move it to the Recycle Bin (" + code + ").");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    public static bool Safe(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return false;
        if (string.Equals(Trim(full), Trim(root), StringComparison.OrdinalIgnoreCase))
            return false;

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Same(full, docs))
            return false;
        var games = Path.Combine(docs, "My Games");
        if (Same(full, games))
            return false;
        if (Same(full, Path.Combine(games, "ArmaReforger")))
            return false;
        if (Same(full, Path.Combine(games, "ArmaReforger", "profile")))
            return false;
        if (Same(full, Path.Combine(games, "ArmaReforgerWorkbench")))
            return false;
        return true;
    }

    static bool Same(string a, string b)
    {
        return string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);
    }

    static string Trim(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
}
