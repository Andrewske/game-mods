# AI Learnings: Ship of Fools mods

## Setup
- Game: Unity 2020.3.16f1, IL2CPP, metadata v27.1, Steam app 1286580, runs under Proton.
- Loader: BepInEx 6 BE build 789 (`BepInEx-Unity.IL2CPP-win-x64`), the Windows build because of Proton.
- The `winhttp=native,builtin` override lives in the prefix registry
  (`compatdata/1286580/pfx/user.reg` under `[Software\\Wine\\DllOverrides]`), so no Steam launch option is needed.
  Backup of the original: `downloads/user.reg.bak`.
- UnityExplorer: the yukieiji fork, asset `UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip`. F7 toggles it in game.
- .NET 8 SDK is installed user-locally in `~/.dotnet` (the system only has runtimes). The BepInEx runtime is .NET 6, so target `net6.0`.
- ilspycmd 8.2 needs `DOTNET_ROLL_FORWARD=Major` (it targets net6, which isn't installed).

## Game code map
- Gameplay code lives in **Core.dll** (global namespace), not Corsair.dll.
- Decompile: `ilspycmd -p -o decomp/Core -r BepInEx/interop BepInEx/interop/Core.dll`.
- Held item: `player.GetHoldableInHands()` (HoldActionExtensions), type checks `IsPlanks()/IsMagazine()/...` (HoldableExtensions).
- Cannon load prompt: `CannonFocusable.CanDoPrimaryAction/PrimaryAction` (the action is "Load" while holding an item, "Use" otherwise).
- Paddle hits: `PaddleAttack.ApplyEffects(GameObject)` runs CollisionEffects such as `FlinchPlayerEffect` (paddle-only),
  `KnockBackEffect` (shared with projectiles), and `DestunPlayerEffect` (frees stunned teammates; keep it).
- Netcode: Unity Netcode for GameObjects (ServerRpc/ClientRpc pairs on most actions).

## Gotchas
- `pkill -f ShipOfFools.exe` kills the calling shell too, because the pattern appears in its own command line. Use `pgrep -x`/`pkill -x ShipOfFools.exe`.
- Interop gives signatures only, no method bodies. Confirm behavior in game or in Ghidra.
- IL2CPP can inline small private methods into their caller, and an inlined method never hits its Harmony hook. `PaddleAttack.ApplyEffects` was one: a flag set there never fired. Hook Unity message methods (`OnTriggerEnter`, `Update`, ...) instead when you need a reliable "we're inside X" window, since the engine calls those directly.
- Verified in game (local co-op, keyboard + controller): plank block works; paddle no longer flinches or shoves teammates (v0.1.1). Island NPCs are `NPC`, not players, so they still react to hits by design.
- Need method bodies? Cpp2IL 2022.0.7 (Linux binary in `tools/cpp2il/`) gives pseudocode + annotated asm:
  `./Cpp2IL-2022.0.7-Linux --game-path "<game>" --exe-name ShipOfFools --run-analysis-for-assembly Core --analysis-level 1 --parallel --disable-registration-prompts --output-root out`
  (~1 min). Read `out/types/Core/method_dumps/<Type>_methods.txt`. Blank conditions like `if ( != 0)` are field reads it couldn't name:
  `objdump -d -M intel --start-address=0x<addr> GameAssembly.dll` shows the offset, then match it in `<Type>_metadata.txt`.
- NGO `NetworkBehaviour` field offsets: 0x1D `IsOwner`, 0x1E `IsServer`, 0x20 `IsHost`, 0x22 `IsSpawned`.

## Guest steering (investigated, not built)
- `BoatHelmFocusable` has no host check. Using the wheel calls `NodeSelectionMenu.ActivateMenu(player)` -> `ActivateMenuServerRpc`, so a guest can open the map and becomes `MainPlayer`.
- But `MapMenu.Update` / `ConfirmSelection` / `SecondaryAction` and `HexMapManager.ChangeSelection` / `UnselectAllTiles` / `SetReachableTiles` / `EndTurn` / `set_Interactable`
  all start with `if (IsSpawned && !IsOwner) return;`. The host owns the map, so guest input is dropped.
- Fix options: host transfers NetworkObject ownership of the map to the guest while they're MainPlayer, or the guest relays select/confirm to the host via NGO named messages.
- Local co-op can't reproduce this (both players live on the host). Testing needs two PCs.

### Guest steering, round 2 (2026-10-23, real two-PC test)
- Guest sees NO wheel prompt at all. Cause: `BoatHelmFocusable.CanDoPrimaryAction` needs `BoatHelm.hasDestination`, a plain (non-networked) bool set by `BoatManager.SetNextDestination`. Its caller `ExplorationController.EnableNavigation` starts with the same `IsSpawned && !IsOwner` gate (objdump 0x180750B1A: `cmp [rcx+0x22]` / `cmp [rcx+0x1d]`). So it only runs on the host, and the guest's helm never gets a destination and the node menu GameObject stays inactive.
- So a guest-steering mod needs two layers: (1) mirror the navigation state to the guest (host sends destination, guest calls `SetNextDestination` + activates the menu), (2) get guest map input through the owner-gated MapMenu/HexMapManager. Both PCs need the mod (custom messages).
- "The handle is invalid" spam in the BepInEx console is Rewired raw input (`<RI> Failed to get input data`), not our plugin. The console window shows Unity log errors; hide it with `[Logging.Console] Enabled = false` in `BepInEx/config/BepInEx.cfg`.
- Windows laptop logs: `C:\Users\kevin\AppData\LocalLow\Fika Productions\ShipOfFools\Player.log`; game in `C:\Program Files (x86)\Steam\steamapps\common\Ship of Fools`.
