using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ds1ItemTracker.Services;

public sealed class MemoryService : IDisposable
{
    // ── Win32 imports ────────────────────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int nSize,
        out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        out MEMORY_BASIC_INFORMATION lpBuffer,
        nuint dwLength);

    // 64-bit MEMORY_BASIC_INFORMATION (without PartitionId; compatible with all Win10+ versions)
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint  AllocationProtect;
        public uint  __alignment1;      // pad to 8-byte align RegionSize
        public ulong RegionSize;
        public uint  State;
        public uint  Protect;
        public uint  Type;
        public uint  __alignment2;
    }

    private const uint PROCESS_VM_READ          = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint MEM_COMMIT               = 0x1000;

    // ── State ────────────────────────────────────────────────────────────────
    private IntPtr _handle     = IntPtr.Zero;
    private long   _moduleBase;
    private int    _moduleSize;

    public bool IsConnected => _handle != IntPtr.Zero && _moduleBase != 0;
    public long ModuleBase  => _moduleBase;
    public int  ModuleSize  => _moduleSize;

    // ── Connection ───────────────────────────────────────────────────────────
    public bool TryConnect(string processName = "DarkSoulsRemastered")
    {
        Disconnect();

        var procs = Process.GetProcessesByName(processName);
        if (procs.Length == 0) return false;

        var proc = procs[0];

        _handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, proc.Id);
        if (_handle == IntPtr.Zero) return false;

        // Find the main module base address and size
        try
        {
            foreach (ProcessModule m in proc.Modules)
            {
                if (m.ModuleName.Equals($"{processName}.exe", StringComparison.OrdinalIgnoreCase))
                {
                    _moduleBase = m.BaseAddress.ToInt64();
                    _moduleSize = m.ModuleMemorySize;
                    break;
                }
            }
        }
        catch
        {
            Disconnect();
            return false;
        }

        if (_moduleBase == 0)
        {
            Disconnect();
            return false;
        }

        return true;
    }

    public void Disconnect()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle     = IntPtr.Zero;
            _moduleBase = 0;
            _moduleSize = 0;
        }
    }

    // ── AOB scan ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Scans [startAddress, startAddress+length) for <paramref name="pattern"/>.
    /// Null entries in the pattern are wildcards.
    /// </summary>
    public List<long> AobScan(long startAddress, int length, byte?[] pattern)
    {
        var hits = new List<long>();
        if (!IsConnected || length <= 0) return hits;

        const int CHUNK = 0x10000; // 64 KB chunks
        var buf = new byte[CHUNK + pattern.Length];

        for (long pos = 0; pos < length; pos += CHUNK)
        {
            int toRead = (int)Math.Min(CHUNK + pattern.Length, length - pos);
            if (!ReadBytes(startAddress + pos, buf, toRead)) continue;

            int scanLen = Math.Max(0, toRead - pattern.Length + 1);
            for (int i = 0; i < scanLen; i++)
            {
                if (PatternMatches(buf, i, pattern))
                    hits.Add(startAddress + pos + i);
            }
        }
        return hits;
    }

    private static bool PatternMatches(byte[] buf, int offset, byte?[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
            if (pattern[i].HasValue && buf[offset + i] != pattern[i]!.Value)
                return false;
        return true;
    }

    // ── Primitive readers ────────────────────────────────────────────────────
    public bool ReadBytes(long address, byte[] buffer, int length = -1)
    {
        if (!IsConnected) return false;
        int size = length < 0 ? buffer.Length : length;
        return ReadProcessMemory(_handle, (IntPtr)address, buffer, size, out _);
    }

    public long ReadInt64(long address)
    {
        var buf = new byte[8];
        return ReadBytes(address, buf) ? BitConverter.ToInt64(buf, 0) : 0L;
    }

    public int ReadInt32(long address)
    {
        var buf = new byte[4];
        return ReadBytes(address, buf) ? BitConverter.ToInt32(buf, 0) : 0;
    }

    public uint ReadUInt32(long address)
    {
        var buf = new byte[4];
        return ReadBytes(address, buf) ? BitConverter.ToUInt32(buf, 0) : 0u;
    }

    public byte ReadByte(long address)
    {
        var buf = new byte[1];
        return ReadBytes(address, buf) ? buf[0] : (byte)0;
    }

    // Reads a contiguous block; returns null on failure
    public byte[]? ReadByteArray(long address, int length)
    {
        var buf = new byte[length];
        return ReadBytes(address, buf) ? buf : null;
    }

    /// <summary>
    /// Returns the committed byte-size of the memory region containing
    /// <paramref name="address"/>.  Returns 0 on failure or uncommitted memory.
    /// </summary>
    public long GetRegionSize(long address)
    {
        if (!IsConnected) return 0;
        nuint sz = (nuint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORY_BASIC_INFORMATION>();
        if (VirtualQueryEx(_handle, (IntPtr)address, out var mbi, sz) == 0)
            return 0;
        if ((mbi.State & MEM_COMMIT) == 0) return 0;
        return (long)mbi.RegionSize;
    }

    public void Dispose() => Disconnect();
}
