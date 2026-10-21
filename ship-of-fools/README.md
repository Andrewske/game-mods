# Ship of Fools Tweaks

A BepInEx plugin for Ship of Fools (Fika Productions, Steam app 1286580).

| Tweak | Config key | Default |
|-------|------------|---------|
| Planks can't be loaded into cannons | `[Cannons] BlockPlankLoading` | on |
| Paddle hits don't knock down or shove teammates (freeing a stunned teammate still works) | `[Paddle] NoFriendlyFire` | on |

Config file: `<game>/BepInEx/config/dev.kevin.shipoffools.tweaks.cfg`.
In co-op, both players need the mod on the same version.

## How it works
The game is Unity built with IL2CPP, so there are no .NET game DLLs to edit.
BepInEx 6 loads into the game through `winhttp.dll`, generates C# "interop"
wrappers for the game's code, and loads our plugin. The plugin uses Harmony
to run our code before or after specific game methods.

## Layout
```
src/ShipOfFoolsTweaks/
  Plugin.cs                         entry point, config, version
  Patches/PlankCannonPatch.cs       cannon "Load" prompt is refused while holding planks
  Patches/PaddleFriendlyFirePatch.cs  skips flinch, knockback and recoil on players during a paddle hit
scripts/package.sh                  builds the Windows zip (BepInEx + plugin + INSTALL.txt)
scripts/INSTALL.txt                 install steps shipped inside the zip
ai-learnings.md                     setup details, game code map, gotchas (read before changing patches)
```
Not committed: `decomp/` (decompiled game code, Fika's copyright), `downloads/`, `tools/`, `release/`.

## Dev loop (this machine)
```bash
export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=$HOME/.dotnet
dotnet build src/ShipOfFoolsTweaks     # also copies the DLL into the game's BepInEx/plugins
```
Then restart the game (`pkill -x ShipOfFools.exe`, never `pkill -f`) and launch from Steam.
- Log: `<game>/BepInEx/LogOutput.log`. Look for `Ship of Fools Tweaks <version> loaded`.
- UnityExplorer is installed locally (not shipped). Press F7 in game to inspect objects.
- Two-player testing without a second person: local co-op with keyboard plus a controller.

`<game>` is `~/.local/share/Steam/steamapps/common/Ship of Fools`.

## Adding a tweak
1. Find the game method: `grep` the decompiled code in `decomp/Core/` (regenerate with the
   ilspycmd command in `ai-learnings.md` if missing). Interop files show signatures only.
2. Add a `ConfigEntry` in `Plugin.cs` and a new file in `Patches/`.
3. Prefer hooking public methods or Unity messages (`OnTriggerEnter`, `Update`).
   Small private methods can be inlined by IL2CPP and never fire their hook.
4. Test in game, ideally local co-op.

## Releasing
1. Bump `Version` in `Plugin.cs`.
2. Branch, commit, PR, merge (main only moves through PRs).
3. `scripts/package.sh` writes `release/ShipOfFoolsTweaks-v<version>.zip`.
4. `gh release create ship-of-fools-v<version> release/ShipOfFoolsTweaks-v<version>.zip --title ... --notes ...`
5. Send Bre the release link. For updates she only needs the new
   `BepInEx/plugins/ShipOfFoolsTweaks/ShipOfFoolsTweaks.dll`, or she can re-extract the whole zip.

## If a game update breaks it
BepInEx regenerates interop on the next launch. Check the log for Harmony errors,
which name the missing method, then find its new name in a fresh decompile.
If BepInEx itself fails, try a newer bleeding-edge build and update `BEPINEX_BUILD` in `package.sh`.
