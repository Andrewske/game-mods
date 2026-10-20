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
