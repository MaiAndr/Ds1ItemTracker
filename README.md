# DS1 Remastered — Item Pickup Tracker

> **[⬇ Download the latest release](https://github.com/MaiAndr/Ds1ItemTracker/releases/latest)**

A real-time item pickup tracker for **Dark Souls Remastered** that reads the game's event flags directly from memory. Automatically detects which items you have picked up and highlights them in a clean dark-themed UI.

---

## Features

- **Live memory reading** — attaches to the running `DarkSoulsRemastered.exe` process and polls every second; no game files are modified
- **~350 verified item locations** — boss kills, boss drops, and world pickups across all areas including the Artorias of the Abyss DLC
- **Auto-switches area** — when you pick up an item, the tracker automatically scrolls to that area
- **Item Randomizer support** — parses the randomizer's `ItemLotParam.param` seed file and remaps all flag IDs so the tracker correctly marks locations you have visited, not the vanilla items
- **Stream overlay (OBS)** — built-in HTTP server serves a lightweight HTML overlay at `http://localhost:7373/`; use it as an OBS Browser Source with transparent background
- **Floating on-screen overlay** — always-on-top borderless window you can drag anywhere; supports click-through mode so you can interact with the game beneath it
- **Flag Scanner** *(debug build only)* — snapshot the flag array, pick up an item, detect which flag changed, and add it directly to `items.json`
- **Editable item database** — all item locations live in `items.json`; add, remove, or verify items without recompiling

---

## Requirements

- Windows 10/11 (x64)
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop Runtime)
- Dark Souls Remastered (Steam)

---

## Usage

1. **Start Dark Souls Remastered** and load into a save file (must be in-game, not the main menu)
2. **Run `Ds1ItemTracker.exe`** — the status bar shows `● Connected` once the game is detected
3. Browse areas on the left; items you have already picked up are shown with a ✓ and strikethrough
4. Items marked ⚠ are unverified — their flag IDs are placeholders and are not tracked

> **Using [Matt's DS1 Enemy Randomizer](https://www.nexusmods.com/darksoulsremastered/mods/922)?**
> Start the game first and let the enemy randomizer fully load, then launch the tracker. The enemy randomizer injects into the process on startup and can interfere with the AoB scan if the tracker connects too early.

### Item Randomizer (HotPocketRemix)

When using [DarkSoulsItemRandomizer](https://github.com/HotPocketRemix/DarkSoulsItemRandomizer), item locations are shuffled but the tracker still needs to match the right flag to each physical location. Use the built-in remapping feature:

1. Generate your randomizer seed as normal — a `random-seed-...` folder will appear in the DS1R game directory
2. In the tracker header, confirm the **game folder** path is correct (auto-detected from Steam by default)
3. The detected seed folder and param file are shown next to the path — click **⟳** to refresh if you generated a new seed
4. Click **Apply** — the tracker reads `ItemLotParam.param` from the seed folder, remaps every location's flag ID, and saves `items.default.json` as a backup
5. The status shows **Randomizer Active** when remapping is in effect
6. Click **Revert** to restore vanilla tracking (e.g. for a new non-randomized run)

### Stream Overlay (OBS)

1. Click the **`http://localhost:7373/`** link in the header (or copy it manually)
2. In OBS: **Add Source → Browser Source** → paste the URL → enable **Transparent background**
3. The overlay auto-updates every 0.5 s and mirrors whichever area is currently selected
4. Use the **Opacity** slider in the tracker header to adjust the card background transparency

### Floating On-Screen Overlay

1. Click **⧉ Float** in the header to open an always-on-top overlay window
2. Drag it by the header to position it on screen
3. Click **⊕** to enable click-through — mouse clicks pass through the item list to the game; the header buttons remain interactive
4. Click **⊕** again to disable click-through; click **✕** to close

---

## How It Works

DS1R stores event flags (item pickups, boss kills, etc.) in a contiguous bit array in memory. The tracker:

1. Uses an **array-of-bytes (AoB) scan** to locate the `SprjEventFlagMan` global pointer in the game executable — this works regardless of patch version without hardcoded offsets
2. Dereferences the pointer chain to find the flag array base address
3. For each tracked item, computes the byte/bit address using the **same addressing formula as the game engine** (derived from [DSAP by ArsonAssassin](https://github.com/ArsonAssassin/DSAP))
4. Flag IDs are sourced from [DarkSoulsItemRandomizer by HotPocketRemix](https://github.com/HotPocketRemix/DarkSoulsItemRandomizer) and [DSAP's resource files](https://github.com/ArsonAssassin/DSAP/tree/main/source/DSAP/Resources)

---

## Adding / Verifying Items (Flag Scanner)

Run a **Debug build** to access the Flag Scanner tab:

1. Load into the game world
2. Click **📷 Take Snapshot** to record the current flag array
3. Pick up the item in-game
4. Click **🔍 Detect Changes** — the exact flag ID appears in the results
5. Click **Add to DB** → fill in the item name and area → the entry is saved to `items.json` and marked verified

---

## items.json

All item locations are defined in `items.json` next to the executable. The structure is:

```json
{
  "version": "3.0",
  "areas": [
    {
      "id": "the_depths",
      "name": "The Depths",
      "items": [
        {
          "id": "dep_sewer_key",
          "name": "Sewer Chamber Key",
          "flagId": 51000240,
          "verified": true,
          "notes": "Leaning against the bars near the Giant Rat"
        }
      ]
    }
  ]
}
```

| Field | Description |
|---|---|
| `flagId` | Event flag ID read from memory. Items with `verified: false` are not tracked. |
| `verified` | `true` = actively tracked. `false` = placeholder, shown as ⚠ |
| `notes` | Optional tooltip text shown on hover |

---

## Building from Source

```bash
git clone https://github.com/MaiAndr/Ds1ItemTracker
cd Ds1ItemTracker
dotnet build -c Release
# Output: bin/Release/net8.0-windows/Ds1ItemTracker.exe
```

Or publish a distributable single-file exe:
```bash
dotnet publish -c Release -r win-x64 --no-self-contained -o publish/
```

Pre-built releases are available on the [Releases page](https://github.com/MaiAndr/Ds1ItemTracker/releases).

---

## Credits

- Flag addressing logic from [DSAP (ArsonAssassin)](https://github.com/ArsonAssassin/DSAP)
- Item lot flag IDs from [DSAP Resources](https://github.com/ArsonAssassin/DSAP/tree/main/source/DSAP/Resources) and [DarkSoulsItemRandomizer (HotPocketRemix)](https://github.com/HotPocketRemix/DarkSoulsItemRandomizer)

---

## Disclaimer

This tool reads game memory (read-only). It does not modify the game, inject code, or communicate with online services. Use at your own risk in accordance with From Software's terms of service.
