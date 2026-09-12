using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PetHelper;

// No path or original exception from this layer is shown in UI or sent to the Host.
internal static class CharacterFiles
{
    internal static string CheckedPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < 3 || full[1] != ':' || full[2] != '\\' || full[2..].Contains(':')) throw CharacterManifest.Invalid();
        if (new DriveInfo(full[..3]).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram))
            throw CharacterManifest.Invalid();
        for (var cursor = full; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw CharacterManifest.Invalid();
        }
        return full;
    }

    internal static string Child(string root, string relative)
    {
        var parent = CheckedPath(root).TrimEnd('\\') + "\\";
        var child = CheckedPath(Path.Combine(parent, relative.Replace('/', '\\')));
        if (!child.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw CharacterManifest.Invalid();
        return child;
    }

    internal static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var full = CheckedPath(path);
        var stream = new FileStream(full, mode, access, share);
        try
        {
            var actual = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(stream.SafeFileHandle, actual, (uint)actual.Capacity, 0);
            if (length == 0 || length >= actual.Capacity ||
                !string.Equals(actual.ToString(), "\\\\?\\" + full, StringComparison.OrdinalIgnoreCase)) throw CharacterManifest.Invalid();
            CheckedPath(full);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    internal static byte[] Read(string path, int maximum)
    {
        using var stream = Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw CharacterManifest.Invalid();
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static void WriteNew(string path, byte[] bytes)
    {
        using var stream = Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(true);
    }

    internal static long Size(string root)
    {
        if (!Directory.Exists(root)) return 0;
        var queue = new Queue<string>();
        queue.Enqueue(CheckedPath(root));
        long size = 0;
        var count = 0;
        while (queue.TryDequeue(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 60000) throw CharacterManifest.Invalid();
                CheckedPath(path);
                if (Directory.Exists(path)) queue.Enqueue(path);
                else size = checked(size + new FileInfo(path).Length);
            }
        }
        return size;
    }

    internal static void DeleteTree(string root, string relative)
    {
        var target = Child(root, relative);
        if (!Directory.Exists(target)) return;
        Size(target); // Reject reparse points before any recursive deletion.
        Directory.Delete(target, true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
}
