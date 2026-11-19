# EvoHelper: a BALL x PIT mod

Shows evolution recipes right inside the game, so you don't need the wiki open while you play.

![Evolutions in the level-up tooltip](docs/screenshots/1-evolutions-panel.png)

## What it does

**On the level-up screen**
- The tooltip of each offered ball or passive lists every evolution it is part of, as icons: `partner = result`. Recipes you can already make are highlighted.
- **Gold border** = not in your encyclopedia yet. A small **+** means the result evolves further.
- **Ready to evolve**: the pairs you already own (each ingredient needs Lv3, then a Fusion Reactor).
- **Character portrait** in the corner of a passive = only that character can find it. Bright if they are in this run (Matchmaker pairs count), grey if not.
- The game's own "Synergies with Equipment" list is hidden while the panel shows, since the panel covers it (`F8` brings it back).

**Details view and HUD:** the game's equipment popup (the level-up screen's Details view, and hovering your balls or passives during a run) also lists that item's evolutions, highlighted by what you own.

**Hover any icon** for its own tooltip: what it is made from, what it evolves into next, and **hold Shift** for the game's description. Tooltips nest, so you can follow a chain all the way up or down. Move the mouse away and they close.

![Nested tooltip: made from and evolves further](docs/screenshots/2-nested-tooltip.png)

**During a run**
- DPS at the top of the screen: all damage that lands on enemies, averaged over the last 10 seconds of run time. That includes baby balls, status effects and passives that deal damage on their own.
- DPS of each ball and passive on its icon: on the pause screen (with an extra entry for baby balls), on the level-up screen's row of current equipment, in the fusion picker, and as a line in the equipment popup. These use the game's own per-item counters, which overlap, so they do not add up to the total.
- Numbers on the health orb and the XP bar.

![DPS, health and XP numbers](docs/screenshots/3-hud-dps-hp-xp.png)

**Keys:** `F8` hides the evolution panel, `F9` hides the DPS and bar numbers.

## Controller

Everything works on a controller too. On the level-up screen:

| Button | Does |
|---|---|
| **R3** (click the right stick) | Start inspecting the icons in the panel (press again to stop) |
| **D-pad / left stick** | Move between icons. The selected icon's tooltip opens straight away |
| **A** | Go into that tooltip's icons, to follow a chain |
| **B** | Back out one level (at the top, stops inspecting) |
| **RT** (hold) | Show descriptions, like Shift on a keyboard |
| **Right stick** | Scroll the panel |

The panel shows these with the game's own button icons, for Xbox, PlayStation and Switch pads. While you're inspecting, the pick screen ignores the controller, so you won't pick an offer by accident. Tooltip hints say "hold RT" instead of "hold Shift" when you're on a pad.

![Controller inspect mode with button hints](docs/screenshots/4-controller-inspect.png)

**Swap the sticks.** Two extra toggles at the bottom of the game's Options > General:
- **Village: camera on left stick, cursor on right** (the game's default is the other way round).
- **Fights: aim with left stick, move with right.**

They only affect a controller, and menu scrolling stays as it is. They're saved in the mod's own config file, `BepInEx\config\schuah.ballxpit.evohelper.cfg`, not in the game's settings.

## Where the recipes come from

Recipes are read from the game's own data, not from a hand-made list, so newer balls and DLC recipes are included, and it stays correct after game updates that only change content. The mod only reads. It never changes your save or the game's state.

## Install (Windows, Steam)

Find the game folder: in Steam, right-click BALL x PIT > Manage > Browse local files. It is the folder with `Balls.exe`.

**Easiest: the all-in-one zip**
1. Download `EvoHelper-vX.Y.Z-with-BepInEx.zip` from [Releases](https://github.com/DoughnutsNCookies/Mod-BallxPit/releases).
2. Extract it into the game folder, so `winhttp.dll` and the `BepInEx` folder sit next to `Balls.exe`.
3. Start the game. The first launch takes a minute or two longer while BepInEx prepares itself. Later launches are normal.

**Already have BepInEx 6 (IL2CPP)?** Download `EvoHelper-vX.Y.Z-plugin-only.zip` and extract it into the game folder. It only adds `BepInEx\plugins\EvoHelper\EvoHelper.dll`.

This mod needs **BepInEx 6 bleeding edge, IL2CPP x64** (tested with build `6.0.0-be.793`). BepInEx 5 and the Mono builds will not work.

### Turn it off or uninstall
- **Turn off all mods:** rename `winhttp.dll` in the game folder to `winhttp.dll.off`. Rename it back to turn them on again.
- **Remove only this mod:** delete `BepInEx\plugins\EvoHelper`.
- **Remove everything:** delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and `changelog.txt` from the game folder.

Steam's "Verify integrity of game files" does not remove the mod.

### If something goes wrong
- After a game update, the first launch is slow again (BepInEx rebuilds its files). If the mod then stops working, the game itself should still run. Open an issue.
- The log is `BepInEx\LogOutput.log` in the game folder. Lines from this mod start with `EvoHelper`. Attach it to any bug report.

## Building from source

Needs the .NET 8 SDK and a game folder with BepInEx installed and run once (it generates the `BepInEx\interop` assemblies the project references).

```
dotnet build src\EvoHelper\EvoHelper.csproj -c Release -p:GameDir="C:\path\to\BALL x PIT"
```

The build copies `EvoHelper.dll` into the game's `BepInEx\plugins\EvoHelper\`. Add `-p:SkipDeploy=true` to only compile (for example while the game is running, since the DLL is locked then).

### macOS (Steam)

The macOS version of the game is a Mono build, not IL2CPP, so the Windows DLL does not load there. `src/EvoHelper.Mono/` builds the same sources for it, against **BepInEx 5** (5.4.23.6 or newer, the `macos_universal` zip). Needs the .NET 10 SDK or newer.

1. Extract BepInEx into the game folder, so `run_bepinex.sh` and the `BepInEx` folder sit next to `Balls.app`. Run `chmod +x run_bepinex.sh`.
2. The game ships trimmed base .NET libraries, which BepInEx cannot patch with. Download the full set for the game's Unity version (`https://unity.bepinex.dev/corlibs/6000.0.62.zip` at the time of writing) and extract it into `BepInEx/unstripped_corlib`. In `run_bepinex.sh`, set `dll_search_path_override` to the full path of that folder.
3. Build. This copies `EvoHelper.dll` into `BepInEx/plugins/EvoHelper/`. `GameDir` defaults to the standard Steam library path.
   ```
   dotnet build src/EvoHelper.Mono/EvoHelper.Mono.csproj -c Release -p:GameDir="/path/to/BALLxPIT"
   ```
4. In Steam, set the game's launch options to `"/path/to/BALLxPIT/run_bepinex.sh" %command%`. Clear them to turn all mods off.

### Source layout (`src/EvoHelper/`)
| File | What it does |
|---|---|
| `Plugin.cs` | Entry point and Harmony hooks on the level-up screen |
| `Evo.cs` | Recipe logic: owned items and levels, offers, ready pairs, encyclopedia check |
| `Overlay.cs` | The evolutions block in the game's tooltip, icon chips, scrolling, nested tooltips |
| `OverlayPad.cs` | Controller inspect mode for the block and its tooltips |
| `OverlayEquip.cs` | Evolutions list and DPS line in the game's equipment popup (Details view, HUD hover) |
| `Info.cs` | "Made from" / "evolves further" lookups and the game's description text |
| `CharLock.cs` | Which character a character-specific passive belongs to |
| `Hud.cs` | DPS, health and XP numbers |
| `Dps.cs` | Damage rates: the hook that counts every hit on an enemy, and the per-item meters |
| `HudItemDps.cs` | Per-item DPS on the pause screen, level-up row and fusion picker icons |
| `StickSwap.cs` | Optional stick swap for the village and fights, and its rows in Options > General |

### Game classes used (from a Cpp2IL dump)
- `InfoDB.I.Heroes` / `.Passives`: every ball and passive. `UpgradeInfo.MergeComponents` (ingredient sets) and `.Evolutions`.
- `UpgradeMgr.I.GetHero/GetPassive`: what you own and at which level. `UpgradeMgr.I.TgtXP`, `.MaxHealth`.
- `LevelUpUI` / `LevelUpBtn`: the pick screen and its offers. `SaveMgr.I.MetaSave.*Stats[i].NumObtained`: encyclopedia.
- `InGameUI.I`: HUD (`HealthFill`, `XPFill`, `_displayedXP`). `BattleSaveData.I.Heroes[*].StatData`: damage dealt per ball.

## Credits and licence

Mod code: MIT licence, see [LICENSE](LICENSE). Not affiliated with the developers of BALL x PIT.

The all-in-one zip includes [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) unchanged, with the .NET runtime it ships with.
