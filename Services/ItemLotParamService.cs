namespace Ds1ItemTracker.Services;

using System.IO;

/// <summary>
/// Parses a DS1R ItemLotParam.param binary file (from the item randomizer's
/// seed folder) and builds a mapping from vanilla flag IDs to the flag IDs
/// that actually fire in the randomized game.
///
/// Binary layout (from HotPocketRemix/DarkSoulsItemRandomizer item_lot_param.py):
///
/// Header (0x30 bytes):
///   0x00  uint32 LE  stringsOffset  (absolute into file)
///   0x04  uint32 LE  dataOffset     (absolute into file — unused here)
///   0x08  uint16     unk
///   0x0A  uint16 LE  rowCount
///   0x0C..0x2F      (rest of header, ignored)
///
/// Row entries start at 0x30, each 12 bytes (little-endian):
///   uint32  lotId
///   uint32  rowDataOffset   ← ABSOLUTE offset into file
///   uint32  stringOffset    ← ABSOLUTE offset into file
///
/// Row data is 0x94 bytes. struct "@8I 8i 8h 8H 8i i i B B 8B c c":
///   0x00  8×uint32  item_ids
///   0x20  8×int32   item_categories
///   0x40  8×int16   item_weights
///   0x50  8×uint16  item_cumuls
///   0x60  8×int32   item_flags (per-drop flags, NOT the pickup flag)
///   0x80  int32     get_item_lot_flag  ← THE PICKUP FLAG WE NEED
///   ...
///
/// The randomizer copies the SOURCE item's get_item_lot_flag into the DESTINATION
/// lot row. For each location in items.json (keyed by vanillaFlag = 50_000_000 +
/// lotId), we read get_item_lot_flag from that lotId's row — that is the flag that
/// fires when the player loots that location in the randomized game.
/// </summary>
public sealed class ItemLotParamService
{
    private const int HEADER_ROWCOUNT_OFFSET = 0x0A;   // uint16 LE
    private const int ROW_ENTRIES_START      = 0x30;   // bytes
    private const int ROW_ENTRY_SIZE         = 12;     // bytes
    private const int GET_ITEM_FLAG_OFFSET   = 0x80;   // within row data

    public record ParseResult(
        Dictionary<long, long> Mapping,
        string Diagnostics,
        string? Error = null)
    {
        public bool Success => Error == null;
    }

    public static ParseResult Parse(string paramPath)
    {
        byte[] file;
        try { file = File.ReadAllBytes(paramPath); }
        catch (Exception ex) { return new(new(), "", $"Cannot read file:\n{ex.Message}"); }

        var diag = new System.Text.StringBuilder();
        diag.AppendLine($"File: {paramPath}  ({file.Length:N0} bytes)");

        if (file.Length < ROW_ENTRIES_START + ROW_ENTRY_SIZE)
            return new(new(), diag.ToString(), "File too small to be a valid PARAM.");

        // Row count is a uint16 at 0x0A (little-endian)
        int rowCount = BitConverter.ToUInt16(file, HEADER_ROWCOUNT_OFFSET);
        diag.AppendLine($"Row count: {rowCount}");

        if (rowCount == 0 || rowCount > 60_000)
            return new(new(), diag.ToString(),
                $"Unexpected row count {rowCount}. Is this an ItemLotParam.param file?");

        var mapping    = new Dictionary<long, long>(rowCount);
        int randomized = 0;
        int skipped    = 0;

        for (int i = 0; i < rowCount; i++)
        {
            int entryOff = ROW_ENTRIES_START + i * ROW_ENTRY_SIZE;
            if (entryOff + ROW_ENTRY_SIZE > file.Length) break;

            uint lotId         = BitConverter.ToUInt32(file, entryOff);
            uint rowDataOffset = BitConverter.ToUInt32(file, entryOff + 4);
            // rowDataOffset is ABSOLUTE (from file start)

            int flagAbsOff = (int)(rowDataOffset + GET_ITEM_FLAG_OFFSET);
            if (flagAbsOff + 4 > file.Length) { skipped++; continue; }

            int getItemLotFlag = BitConverter.ToInt32(file, flagAbsOff);

            // Compute the vanilla flag key matching items.json's flagId convention:
            //   World pickup lots (lotId >= 100_000): vanillaFlag = 50_000_000 + lotId
            //     e.g. lot 1000240 → 51000240  (Sewer Chamber Key)
            //   Boss drop lots (lotId 1_000–9_999): vanillaFlag = 50_000_000 + (lotId - 1_000)
            //     e.g. lot 2670 → 50001670  (Orange Charred Ring / Ceaseless Discharge)
            //   This matches the Flag field in DSAP's ItemLots.json exactly.
            long vanillaFlag = (lotId >= 1_000 && lotId <= 9_999)
                ? 50_000_000L + (lotId - 1_000)
                : 50_000_000L + lotId;
            long actualFlag  = (long)(uint)getItemLotFlag; // treat as unsigned

            if (actualFlag == 0) { skipped++; continue; }

            mapping[vanillaFlag] = actualFlag;
            if (vanillaFlag != actualFlag) randomized++;
        }

        diag.AppendLine($"Rows mapped: {mapping.Count}  |  Skipped: {skipped}");
        diag.AppendLine($"Rows with changed flag (randomized): {randomized}");

        // Show a sample of randomized entries for debugging
        int shown = 0;
        foreach (var kv in mapping.Where(kv => kv.Key != kv.Value).Take(5))
            diag.AppendLine($"  e.g. vanilla {kv.Key} → actual {kv.Value}");

        if (mapping.Count == 0)
            return new(new(), diag.ToString(),
                "No rows could be parsed. Wrong file or corrupted data.");

        if (randomized == 0)
            diag.AppendLine("⚠ No flags changed — this may be a vanilla (unrandomized) param.");

        return new(mapping, diag.ToString());
    }
}
