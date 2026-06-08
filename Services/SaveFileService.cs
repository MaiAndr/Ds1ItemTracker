namespace Ds1ItemTracker.Services;

using System.IO;

/// <summary>
/// Reads and diffs DS1R SL2 save files to help locate event flag storage.
///
/// The SL2 is a BND4 container. We treat it as a raw byte blob for diffing —
/// no need to parse the container format to find changed regions.
///
/// File location:
///   %APPDATA%\DarkSoulsRemastered\&lt;SteamID&gt;\DRAKS0005.sl2
/// </summary>
public sealed class SaveFileService
{
    private byte[]? _snapshot;
    private string  _loadedPath = string.Empty;

    public string LoadedPath => _loadedPath;
    public bool   HasSnapshot => _snapshot != null;

    // ── Auto-detect ──────────────────────────────────────────────────────────
    public static string? AutoDetectSavePath()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string root    = Path.Combine(roaming, "DarkSoulsRemastered");
        if (!Directory.Exists(root)) return null;

        // Under the root there are SteamID subdirectories
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            string candidate = Path.Combine(dir, "DRAKS0005.sl2");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // ── Load ─────────────────────────────────────────────────────────────────
    public (bool ok, string error) Load(string path)
    {
        try
        {
            // Use ReadWrite share so the game can keep the file open
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[fs.Length];
            int read = fs.Read(buf, 0, buf.Length);
            if (read != buf.Length)
                return (false, $"Only read {read}/{buf.Length} bytes.");

            _loadedPath = path;
            // Don't store as snapshot yet — just return the bytes
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ── Snapshot ─────────────────────────────────────────────────────────────
    public (bool ok, string error, int size) TakeSnapshot(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _snapshot = new byte[fs.Length];
            int read  = fs.Read(_snapshot, 0, _snapshot.Length);
            if (read != _snapshot.Length)
            {
                _snapshot = null;
                return (false, $"Only read {read}/{_snapshot?.Length} bytes.", 0);
            }
            _loadedPath = path;
            return (true, string.Empty, _snapshot.Length);
        }
        catch (Exception ex)
        {
            _snapshot = null;
            return (false, ex.Message, 0);
        }
    }

    public void ClearSnapshot() => _snapshot = null;

    // ── Diff ─────────────────────────────────────────────────────────────────
    public record ByteRegion(int Offset, int Length, byte[] Before, byte[] After)
    {
        public string OffsetHex    => $"0x{Offset:X8}";
        public string EndHex       => $"0x{Offset + Length - 1:X8}";
        public string Summary      => $"{OffsetHex}–{EndHex}  ({Length} B changed)";
        public string BeforeHex    => ToHex(Before, 32);
        public string AfterHex     => ToHex(After, 32);

        private static string ToHex(byte[] b, int max)
        {
            int take = Math.Min(b.Length, max);
            string hex = BitConverter.ToString(b, 0, take).Replace("-", " ");
            return b.Length > max ? hex + " …" : hex;
        }
    }

    /// <summary>
    /// Reads the save file at <paramref name="path"/> and diffs it against the snapshot.
    /// Changed bytes are grouped into contiguous regions (gap ≤ <paramref name="mergeGap"/>).
    /// </summary>
    public (List<ByteRegion> regions, string error) Diff(string path, int mergeGap = 16)
    {
        if (_snapshot == null)
            return (new(), "No snapshot taken yet.");

        byte[] current;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            current = new byte[fs.Length];
            int read = fs.Read(current, 0, current.Length);
            if (read != current.Length)
                return (new(), $"Only read {read}/{current.Length} bytes.");
        }
        catch (Exception ex)
        {
            return (new(), ex.Message);
        }

        int compareLen = Math.Min(_snapshot.Length, current.Length);
        var changes    = new List<int>();
        for (int i = 0; i < compareLen; i++)
            if (_snapshot[i] != current[i])
                changes.Add(i);

        if (changes.Count == 0)
            return (new(), string.Empty);

        // Merge nearby changed offsets into contiguous regions
        var regions = new List<ByteRegion>();
        int start   = changes[0];
        int end     = changes[0];

        for (int i = 1; i < changes.Count; i++)
        {
            if (changes[i] - end <= mergeGap)
            {
                end = changes[i];
            }
            else
            {
                regions.Add(BuildRegion(start, end, _snapshot, current));
                start = end = changes[i];
            }
        }
        regions.Add(BuildRegion(start, end, _snapshot, current));

        return (regions, string.Empty);
    }

    private static ByteRegion BuildRegion(int start, int end, byte[] before, byte[] after)
    {
        int len    = end - start + 1;
        var bBuf   = new byte[len];
        var aBuf   = new byte[len];
        Array.Copy(before, start, bBuf, 0, len);
        Array.Copy(after,  start, aBuf, 0, len);
        return new ByteRegion(start, len, bBuf, aBuf);
    }
}
