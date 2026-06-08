using System.Numerics;

namespace Ds1ItemTracker.Services;

/// <summary>
/// Reads DS1 Remastered event flags, using the exact same logic as
/// ArsonAssassin/DSAP (AddressHelper.cs + Memory.cs).
///
/// Resolution chain:
///   1. AoB scan for the unique byte sequence around "mov rcx, [rip+rel32]"
///      that loads the SprjEventFlagMan global pointer.
///   2. Read the 4-byte RIP-relative displacement at instrAddr+3.
///   3. staticPtrAddr = instrAddr + 7 + rel32   (RIP-relative deref)
///   4. sprjEventFlagMan = ReadUInt64(staticPtrAddr)
///   5. flagArrayBase = ReadUInt32(sprjEventFlagMan)  ← first 4 bytes of the object
///
/// Per DSAP/Memory.cs: ReadFromPointer(addr, 4, depth=1) reads 4 bytes
/// directly at addr (depth decrements to 0 immediately, returns without
/// recursing). So flagArrayBase is the uint32 stored at offset 0 of the
/// SprjEventFlagMan object — a 32-bit absolute address for the flag array.
///
/// Flag addressing:
///   Flags are NOT a simple flat bit-array. The address of flagId is computed
///   by parsing the 8-digit decimal representation of flagId into table offsets.
///   See GetEventFlagOffset() — ported verbatim from DSAP/AddressHelper.cs.
/// </summary>
public sealed class GameFlagService
{
    // ── AoB pattern (from DSAP AddressHelper.cs → EventFlagsAoB) ─────────────
    // Matches the unique instruction sequence:
    //   48 8B 0D ??  ?? ?? ??   mov rcx, [rip+rel32]
    //   99                      cdq
    //   33 C2                   xor eax, edx
    //   45 33 C0                xor r8d, r8d
    //   2B C2                   sub eax, edx
    //   8D 50 F6                lea edx, [rax-10]
    private static readonly byte?[] AOB_PATTERN =
    {
        0x48, 0x8B, 0x0D, null, null, null, null,
        0x99,
        0x33, 0xC2,
        0x45, 0x33, 0xC0,
        0x2B, 0xC2,
        0x8D, 0x50, 0xF6
    };

    // Scan 16 MB from module base — same as DSAP
    private const int SCAN_SIZE = 0x1000000;

    // ── State ────────────────────────────────────────────────────────────────
    private readonly MemoryService _mem;
    private ulong  _flagArrayBase;
    private string _diagInfo = "Not yet resolved.";
    public  string  DiagInfo => _diagInfo;

    public GameFlagService(MemoryService mem) => _mem = mem;

    // ── Resolution ────────────────────────────────────────────────────────────
    private bool Resolve()
    {
        _flagArrayBase = 0;
        if (!_mem.IsConnected) { _diagInfo = "Not connected."; return false; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Module base: 0x{_mem.ModuleBase:X}");

        // Step 1: AoB scan
        var hits = _mem.AobScan(_mem.ModuleBase, SCAN_SIZE, AOB_PATTERN);
        sb.AppendLine($"AoB hits: {hits.Count}");

        foreach (long instrAddr in hits)
        {
            // Step 2: read the 4-byte signed rel32 at instrAddr+3
            var relBuf = new byte[4];
            if (!_mem.ReadBytes(instrAddr + 3, relBuf)) continue;
            int rel32 = BitConverter.ToInt32(relBuf, 0);

            // Step 3: RIP = instrAddr + 7 (instruction length); static ptr address
            long staticPtrAddr = instrAddr + 7 + rel32;
            sb.AppendLine($"  instrAddr=0x{instrAddr:X}  rel32={rel32}  staticPtr=0x{staticPtrAddr:X}");

            // Step 4: read 8-byte pointer at staticPtrAddr → SprjEventFlagMan object
            long sprjEventFlagMan = _mem.ReadInt64(staticPtrAddr);
            sb.AppendLine($"  sprjEventFlagMan=0x{sprjEventFlagMan:X}");
            if (sprjEventFlagMan == 0) continue;

            // Step 5: read first 4 bytes of the object as uint32 → flagArrayBase
            // (ReadFromPointer(addr, 4, depth=1) in DSAP: reads 4 bytes at addr directly)
            uint base32 = _mem.ReadUInt32(sprjEventFlagMan);
            sb.AppendLine($"  flagArrayBase32=0x{base32:X}");
            if (base32 == 0) continue;

            _flagArrayBase = (ulong)base32;
            sb.AppendLine($"\n✓ Resolved — flagArrayBase=0x{_flagArrayBase:X}");
            _diagInfo = sb.ToString();
            return true;
        }

        sb.AppendLine("\n✗ Could not resolve flag array base.");
        sb.AppendLine("  Ensure you are fully loaded into the game world (not the main menu).");
        _diagInfo = sb.ToString();
        return false;
    }

    private bool EnsureBase()
    {
        if (_flagArrayBase != 0) return true;
        return Resolve();
    }

    // ── Flag offset calculation (verbatim from DSAP AddressHelper.cs) ─────────
    /// <summary>
    /// Returns (byteOffset, bitIndex) for the given flagId.
    /// byteOffset is the byte index into the flag array.
    /// bitIndex is which bit within that byte (0=LSB).
    /// </summary>
    public static (ulong byteOffset, int bitIndex) GetEventFlagOffset(int flagId)
    {
        string id  = flagId.ToString("D8");          // pad to exactly 8 digits
        int    tail = int.Parse(id.Substring(5, 3)); // digits [5..7]

        uint mask4 = 0x80000000u >> (tail % 32);
        int sigByte;
        if      ((mask4 & 0x000000FF) != 0) sigByte = 0;
        else if ((mask4 & 0x0000FF00) != 0) sigByte = 1;
        else if ((mask4 & 0x00FF0000) != 0) sigByte = 2;
        else                                sigByte = 3;

        int bitIndex = BitOperations.TrailingZeroCount((mask4 >> (sigByte * 8)) & 0xFF);

        int offset = GetPrimaryOffset(id)
                   + GetSecondaryOffset(id)
                   + int.Parse(id.Substring(4, 1)) * 128
                   + (tail - (tail % 32)) / 8;

        return ((ulong)(offset + sigByte), bitIndex);
    }

    private static int GetPrimaryOffset(string id) => id[0] switch
    {
        '0' => 0x00000,
        '1' => 0x00500,
        '5' => 0x05F00,
        '6' => 0x0B900,
        '7' => 0x11300,
        _   => throw new ArgumentException($"No primary offset for flag: {id}")
    };

    private static int GetSecondaryOffset(string id)
    {
        int n = id.Substring(1, 3) switch
        {
            "000" => 00,
            "100" => 01,
            "101" => 02,
            "102" => 03,
            "110" => 04,
            "120" => 05,
            "121" => 06,
            "130" => 07,
            "131" => 08,
            "132" => 09,
            "140" => 10,
            "141" => 11,
            "150" => 12,
            "151" => 13,
            "160" => 14,
            "170" => 15,
            "180" => 16,
            "181" => 17,
            _     => throw new ArgumentException($"No secondary offset for flag: {id}")
        };
        return n * 1280;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public bool ReadFlag(int flagId)
    {
        if (!EnsureBase()) return false;
        try
        {
            var (byteOffset, bitIndex) = GetEventFlagOffset(flagId);
            byte b = _mem.ReadByte((long)(_flagArrayBase + byteOffset));
            return (b & (1 << bitIndex)) != 0;
        }
        catch (ArgumentException)
        {
            return false;  // unsupported flag ID range
        }
    }

    public Dictionary<long, bool> ReadFlags(IReadOnlyCollection<long> flagIds)
    {
        var result = new Dictionary<long, bool>(flagIds.Count);
        if (!EnsureBase()) return result;

        foreach (long id in flagIds)
        {
            try
            {
                var (byteOffset, bitIndex) = GetEventFlagOffset((int)id);
                byte b = _mem.ReadByte((long)(_flagArrayBase + byteOffset));
                result[id] = (b & (1 << bitIndex)) != 0;
            }
            catch (ArgumentException)
            {
                result[id] = false;
            }
        }
        return result;
    }

    // ── Scanner: raw byte snapshot / diff ─────────────────────────────────────
    // The flag array is ~93 KB max. We snapshot the raw bytes and diff them,
    // then reverse-map changed bit positions back to flag IDs.
    // This avoids iterating millions of flag IDs (most of which are invalid).
    private const int FLAG_ARRAY_BYTES = 0x20000; // 128 KB, covers all known flags

    // Valid first digits and secondary digit[1..3] strings per DSAP AddressHelper
    private static readonly int[]    VALID_PRIMARY   = { 0, 1, 5, 6, 7 };
    private static readonly string[] VALID_SECONDARY = {
        "000","100","101","102","110","120","121","130","131","132",
        "140","141","150","151","160","170","180","181"
    };

    // Lazy reverse map: (byteOffset, bitIndex) → flagId
    private static Dictionary<(ulong, int), int>? _reverseMap;
    private static Dictionary<(ulong, int), int> GetReverseMap()
    {
        if (_reverseMap != null) return _reverseMap;
        var map = new Dictionary<(ulong, int), int>(capacity: 950_000);
        foreach (int p in VALID_PRIMARY)
            foreach (string s in VALID_SECONDARY)
                for (int d4 = 0; d4 <= 9; d4++)
                    for (int tail = 0; tail <= 999; tail++)
                    {
                        // Reconstruct the 8-digit flag ID
                        int flagId = p * 10_000_000
                                   + int.Parse(s) * 10_000
                                   + d4 * 1_000
                                   + tail;
                        try
                        {
                            var key = GetEventFlagOffset(flagId);
                            map.TryAdd(key, flagId);
                        }
                        catch { }
                    }
        _reverseMap = map;
        return _reverseMap;
    }

    /// <summary>Read a raw snapshot of the entire flag array (~128 KB).</summary>
    public byte[]? TakeRawSnapshot()
    {
        if (!EnsureBase()) return null;
        return _mem.ReadByteArray((long)_flagArrayBase, FLAG_ARRAY_BYTES);
    }

    /// <summary>
    /// Diff two raw snapshots and return all flag IDs whose bit transitioned
    /// false→true (newly set).
    /// </summary>
    public static List<int> FindNewlySetFlags(byte[] before, byte[] after)
    {
        var reverseMap = GetReverseMap();
        var results    = new List<int>();
        int len = Math.Min(before.Length, after.Length);

        for (int byteIdx = 0; byteIdx < len; byteIdx++)
        {
            byte diff = (byte)(~before[byteIdx] & after[byteIdx]); // bits that flipped 0→1
            if (diff == 0) continue;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((diff & (1 << bit)) == 0) continue;
                if (reverseMap.TryGetValue(((ulong)byteIdx, bit), out int flagId))
                    results.Add(flagId);
            }
        }
        return results;
    }

    public void Reset() => _flagArrayBase = 0;
}
