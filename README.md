THIS IS SLOP.

# XIV Job Gamba

A Dalamud plugin that spins a wheel of your saved Final Fantasy XIV gearsets and equips the one it lands on.

The wheel is drawn with ImGui: one colored slice per entry, labeled with the job abbreviation, a job icon when the slice is wide enough, a pointer fixed at the top, and a spin that eases out. Slice color comes from the job's role on the ClassJob sheet: tanks blue, healers green, melee red, physical ranged yellow, casters purple, crafters (Disciples of the Hand) grey, and gatherers (Disciples of the Land) dark grey. Two neighboring slices of the same role use different shades, and the label switches between light and dark text so it stays readable. A job that just landed and is now on cooldown is drawn desaturated.

## Commands

| Command | What it does |
| --- | --- |
| `/gamba` | Open or close the wheel. |
| `/gamba config` | Open settings. `/gamba settings` does the same. |
| `/gamba help` | Print the command list in chat. |

The plugin installer cog opens settings (`OpenConfigUi`). The installer's main-UI button opens the wheel.

## What appears on the wheel

The wheel only draws entries the next spin can land on.

**Default:** every saved gearset is its own slice, in slot order. Two Paladin sets both show up, with their own names.

**One slice per job** (settings): sets that share a job collapse to one slice. The highest item level wins. If they tie, the lowest slot number wins.

**Crafters and gatherers** are included unless you turn that off. A crafter or gatherer is a ClassJob row whose `DohDolJobIndex` is zero or higher.

**Blacklist:** settings lists every saved gearset with a checkbox. A checked set never appears. The saved key is the slot number plus the exact gearset name (`3:Raid set`). Renaming the set, or saving a different set into that slot, drops the exclusion. Unmatched exclusions are listed at the bottom of settings and can be removed in one click.

**Cooldown:** settings has a "Cooldown spins" slider. `X` is how many successful picks are remembered as blocked. A job that appears in those last `X` picks stays off the wheel, including every gearset for that job, until `X` newer picks have pushed it out. `0` disables the cooldown. The default is `3`.

History is the list of job ids from spins that actually equipped. If the game refuses the equip, that spin is not recorded and the job stays eligible. History is capped at 100 entries and is stored in the plugin config, so it survives reloads. **Clear history** is on the wheel (whenever anything has been recorded) and in settings.

If every remaining job is on cooldown, the wheel says so and the spin button stays disabled until you clear history, lower `X`, or wait until a job ages out. The same message covers the case where you have fewer distinct jobs than `X`.

## Equipping

When the wheel stops, the plugin calls `RaptureGearsetModule.EquipGearset(slot, 0)` on the framework thread. Passing `0` uses the glamour plate linked to that gearset, the same as equipping it by hand.

The call is skipped, and a chat line explains why, when you are not logged in or the client is in a state that rejects a gearset change: combat, dead or unconscious, casting, zoning, a cutscene, crafting, gathering, mounted, trading, occupied (events, quests, the summoning bell), jumping, diving, a minigame, or a performance. Being in a duty is not one of those. You can still spin between pulls. `EquipGearset` has its own checks and returns `-1` when it refuses; that also shows a chat line and does not count as a pick.

## Building

You need the .NET 10 SDK and the Dalamud reference assemblies (API level 15, `Dalamud.NET.Sdk` 15.0.0).

XIVLauncher keeps those assemblies in:

- Windows: `%AppData%\XIVLauncher\addon\Hooks\dev\`
- Linux: `~/.xlcore/dalamud/Hooks/dev/`
- macOS: `~/Library/Application Support/XIV on Mac/dalamud/Hooks/dev/`

If that folder is somewhere else, set `DALAMUD_HOME` to the directory that contains `Dalamud.dll`. Without the game installed, download [dalamud-distrib/latest.zip](https://goatcorp.github.io/dalamud-distrib/latest.zip) and extract it so the DLLs sit directly in that directory.

The project sets `EnableWindowsTargeting`, so `dotnet build` works on Linux and macOS as well as Windows. From the repo root:

```bash
dotnet build
dotnet test
```

`dotnet build` produces `XIVJobGamba/bin/x64/Debug/XIVJobGamba.dll`. The manifest `XIVJobGamba.json` is written next to the DLL. `dotnet build -c Release` writes the same pair under `XIVJobGamba/bin/x64/Release/`, plus `XIVJobGamba/bin/x64/Release/XIVJobGamba/latest.zip` containing the DLL and the manifest.

`dotnet test` covers the cooldown window, the all-on-cooldown case, blacklist and crafter filtering, one-slice-per-job selection, and the wheel landing on the slice it was aimed at. It does not start the game.

## Installing as a dev plugin

1. Build, and note the full path to `XIVJobGamba.dll`. The json manifest has to stay beside the DLL. The build puts it there.
2. In game, run `/xlsettings` (or `xlsettings` in the Dalamud console). Under **Experimental**, add that DLL path to **Dev Plugin Locations**.
3. Run `/xlplugins`. Open **Dev Tools → Installed Dev Plugins** and enable **XIV Job Gamba**.
4. The cog on that installer entry opens settings. `/gamba` opens the wheel.

You only add the dev path once. After that, enable or disable the plugin from the installer. Rebuild, then disable and enable the plugin (or `/xlplugins` reload) to pick up a new DLL.

## API assumptions

Built against Dalamud `15.0.3` (API level 15, `Dalamud.NET.Sdk` 15.0.0) and the matching `FFXIVClientStructs` from the `latest` distrib.

- Saved sets come from `RaptureGearsetModule.Instance()`, `Entries`, `IsValidGearset`, and `GearsetFlag.Exists`. The slot index passed to `EquipGearset` is the entry index. `CurrentGearsetIndex` is only used for the "equipped now" line.
- Job names and abbreviations come from the Lumina `ClassJob` sheet. Slice role uses `JobType` when it is set (1 tank, 2 pure healer, 3 melee, 4 physical ranged, 5 magical ranged, 6 barrier healer). Base classes often leave `JobType` at 0, so those use `Role` (1 tank, 2 melee, 3 ranged, 4 healer). Ranged is split by the job's `ClassJobCategory`: Disciple of War is physical ranged, Disciple of Magic is magical ranged, with `PrimaryStat` (dexterity vs intelligence) if the category is mixed. Crafters and gatherers both have `DohDolJobIndex >= 0` and share index numbers, so Hand vs Land comes from that category row (hand columns vs miner/botanist/fisher). If the category row cannot be read, UI priority in the 100s is hand and the 200s is land. The "include crafters and gatherers" filter is still `DohDolJobIndex >= 0`.
- Icons prefer `GetClassJobIconForGearset`. If that returns nothing, the icon id is `62100 + class job id`, which is the offset Dalamud's own sample uses. Icons are loaded with `ITextureProvider.TryGetFromGameIcon` and drawn upright on the slice.
- Gearset reads and equips run on the framework thread. The UI keeps a managed copy and does not hold pointers across frames.
- Plugin services are constructor-injected (`IDalamudPluginInterface`, commands, textures, client state, player state, object table, conditions, data, framework, chat, log). Config is `IPluginConfiguration` saved through `SavePluginConfig`.
