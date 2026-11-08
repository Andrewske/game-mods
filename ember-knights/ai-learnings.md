# AI Learnings: Ember Knights mods

## Setup
- Game: Unity 2022.3.62f3, IL2CPP, metadata v31, Steam app 1135230, runs under Proton on Kevin's PC.
- Install: `~/.local/share/Steam/steamapps/common/EmberKnights` (Proton sees it as `S:\steamapps\common\EmberKnights`).
- Save: `EmberKnights_64_Data/SaveData/rog.dat` (unencrypted typed key/value binary; auto backups in `SaveData/Backups/`).
  Steam Cloud is on, so edit saves with the game closed and back them up first.
- Logs: `compatdata/1135230/pfx/drive_c/users/steamuser/AppData/LocalLow/Doom Turtle/EmberKnights/Player.log` (+ `Player-prev.log`).
- Netcode: Photon (`XNetworkPhoton`) with the game's own packet layer in `XNetworkSYS` (C2S_/S2C_ handlers). Kevin hosts, Bre joins online.
- Loader plan: BepInEx 6 IL2CPP, same as ../ship-of-fools (not installed yet).

## Decompiling
- Cpp2IL 2022.0.7 (ship-of-fools/tools) fails: "We support 24-29, got 31".
- Cpp2IL 2022.1.0-pre-release.21 in `tools/cpp2il/` works, ~5 s per format:
  `--game-path <game> --exe-name EmberKnights_64 --output-as diffable-cs|isil --output-to decomp/<name>`
- `decomp/cs/DiffableCs/Assembly-CSharp/GameCode.cs` holds nearly all gameplay types (internal classes in one file).
- `decomp/isil/IsilDump/Assembly-CSharp/GameCode_NestedType_<Type>.txt` has disassembly + ISIL with resolved call names.
  Coroutine state machines (`<...>d__N.MoveNext`) don't get ISIL; read the raw asm.

## Save / resume (built-in)
- Pause menu "Save And Quit" + main menu "Resume Saved Single Player / Local Multiplayer" already exist, offline only.
- `GameUtils.CanSaveState(XWorld)` requires `GameCode.XNetwork.IsLocal`, blocks areas Nexus(6)/Credits(7)/PAXCompetition(12),
  Daily Dungeon, Boss Rush, and checks `currentRoomCMP.roomType` / `roomState` (exact allowed rooms not decoded yet).
- `GameUtils.GetSaveState(XWorld, bool)` builds `SinglePlayerSavedState` (seed, area, room id, XGameState, shop/healing/offering
  room data, base64 blobs) and calls `XNetworkSYS.Serialize_RejoinData`, the online reconnect serializer. It also checks IsLocal.
- Stored in `SavedPlayerSlot.SavedState` / `.MultiplayerSavedState` (save keys `SavedState`, `MultiplayerSavedState`, null = `0x13 ff ff ff ff`).
- Resume: `GameInst.ResumeSavedStateCR(sd, List<ResumeSavedStateLocalMPPlayerData>)` maps saved players to local Rewired ids,
  then `ResumeSavedState_PostFixupPlayers`. Loading goes through `CommonStart_BeginLoadingCR(area, fromReconnect, fromSaveState)`.

## Reconnect / unlocks
- Host log lines: "StartGameWaitForReconnects", "Host sending N rejoin datas after area loading" (rejoins are queued until area load).
- Rejoin failures show on the client, so get Bre's Player.log when one fails.
- Host pushes some rewards to clients: S2C_WeaponUnlock, S2C_EmberRewardResponse, S2C_StartEmberDustSequenceResponse,
  S2C_EveeAbilityUpgradeUnlockedSequence. Other unlocks are probably saved by the host only (unconfirmed).
