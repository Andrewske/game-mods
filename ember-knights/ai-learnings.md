# AI Learnings: Ember Knights mods

## Setup
- Game: Unity 2022.3.62f3, IL2CPP, metadata v31, Steam app 1135230, runs under Proton on Kevin's PC.
- Install: `~/.local/share/Steam/steamapps/common/EmberKnights` (Proton sees it as `S:\steamapps\common\EmberKnights`).
- Save: `EmberKnights_64_Data/SaveData/rog.dat` (unencrypted typed key/value binary; auto backups in `SaveData/Backups/`).
  Steam Cloud is on, so edit saves with the game closed and back them up first.
- Logs: `compatdata/1135230/pfx/drive_c/users/steamuser/AppData/LocalLow/Doom Turtle/EmberKnights/Player.log` (+ `Player-prev.log`).
- Netcode: Photon (`XNetworkPhoton`) with the game's own packet layer in `XNetworkSYS` (C2S_/S2C_ handlers). Kevin hosts, Bre joins online.
- Loader: BepInEx 6 be.789 (IL2CPP win-x64), installed 2026-11-09 and confirmed working on metadata v31.1
  (stable BepInEx rejects v31). First launch builds interop in ~1 min. Same `winhttp=native,builtin` prefix override as
  ship-of-fools, in `compatdata/1135230/pfx/user.reg`. Backups of the original user.reg and rog.dat: `downloads/backups/`.
- Plugin: `src/EmberKnightsTweaks` (`dotnet build -c Release` deploys to BepInEx/plugins). Needs `~/.dotnet` on PATH.
  Tests for the game-free logic: `dotnet test src/EmberKnightsTweaks.Tests`.
- The game's own "[Echo]" lines in Player.log go through Debug.LogError; they are not errors.
- No existing Ember Knights mod scene (no Thunderstore community). GitHub "trainer" repos look like malware bait.
- Bre's laptop: `slonk-n-breh-1` (Tailscale 100.86.233.106, Windows). No SSH server; get files via Taildrop.

## Save format (save-tool/)
- `ts_binary.py` decodes/encodes TSBinary (`TSType` enum in decomp `TSType.cs`): 1-byte tag + payload, Object/List =
  int32 count (-1 = null), Object keys and strings = 7-bit length + UTF-8, DateTime = int64. Lists tag every element.
  Round trip is byte-exact on the live save and all 11 backups.
- Root: SaveVersion 59, GameVersion "2.3.1", Options, `Slots` (list of 3, null = unused), InputMapping1-4.
  Kevin plays slot 0. Slot 1 holds fresh-save defaults (1 weapon, 7 skills, 43 relics), a handy baseline.
- `save_view.py summary|dump` sorts the 115 slot keys into unlock / story / currency / stats / telemetry / session.
  The save does not separate solo from co-op progress, so unlock sync = per-item union/max, leaving stats alone.
- Tests: `uv run --no-project --with pytest pytest -q` (synthetic saves only; never commit a real rog.dat).

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

## Online save / resume mod (v0.3.0)
- Verified 2026-11-09 (solo online): Save And Quit online writes `SavedState`; resuming from the online lobby put the host
  back in the saved run. Rejoin by a second player (Bre) is still untested.
- Hotkeys are unreliable: legacy `Input` saw other keys but never F9 (laptop Fn layer or Proton), so v0.3.0 uses a
  cloned lobby button instead. Resume button = `Instantiate(Refs.HostStartGameButton.gameObject, same parent)`,
  `SetText`, `onClick.RemoveAllListeners()`, `SetCallback(UnityAction)`; click sets a flag and calls
  `HostStartGameClicked`, so the normal countdown runs, and the `StartGame_Internal` prefix resumes instead.
  The copy needs `UIOptionsButtonRefs.Initialize()` (SetContent runs it on Refs.AllButtons: default anim + select/deselect
  highlight hooks), or it stays lit, and `SetupNavigation` wires explicit Unity navigation
  Start <-> Invite, so a postfix splices Start <-> Resume <-> next (controller nav verified v0.3.1).
- A saved run has no player identity: `SavedStatePlayerData` = PlayerIndex, Difficulty, EasyModeDeathCounter,
  WeaponChallengesData, WeaponDefId, ColorInfo. Matching saves to lobby members would need a mod-written sidecar.
- `XNetworkBase.get_IsLocal` = `_connectionType` (0x58) is not Server(1)/Client(2). `OnlineSavePatch` sets it to None
  around `CanSaveState` / `GetSaveState` on the host only, then restores it. Not inlined (both are real calls).
- CanSaveState's room rule: `currentRoomCMP` (World+0xB0) roomState (0x6C) == 116 or 7, or roomType (0x68) == 11.
- Pause > Main Menu (`UIPause.<ClickedMainMenu>b__27_0`): CanSaveState -> GetSaveState -> stored in
  `CurrentSaveSlot.MultiplayerSavedState` if `World.AllPlayers.Count > 1`, else `.SavedState` -> SaveGameData ->
  `SendHostClosedGame` when hosting.
- `UIMainMenu.ClickedResumeSingle`: takes `GameInst.CurrentSaveSlot.SavedState`, nulls it + saves, `DecodeData(World.GameMods)`,
  list of one `ResumeSavedStateLocalMPPlayerData { PlayerIndex 0, RWPlayerId 0 }`, `GameCode.Run(Game.ResumeSavedStateCR(...))`.
  `OnlineResumePatch` does the same from the online lobby (`UIPlayerSelectOnline.Tick`, host, F9) but keeps the save.
- Solo online lobbies were blocked on purpose in hotfix 2.3.1 ("players could start online multiplayer lobbies by
  themselves"): `UIPlayerSelectOnline.CanStartGame` returns Joined count > 1. `SoloOnlineLobbyPatch` (AllowSoloOnline)
  undoes that for the host, for solo testing only; reason for the block is unknown, so turn it off for real runs.
- Online start (`UIPlayerSelectOnline.StartGame_Internal`): host sends char data + save slot to all peers, then StartGameCR.
  Resume sends nothing to peers yet, so Bre has to come in through the rejoin path (step 3, untested).
- ResumeSavedStateCR body (raw asm, Cpp2IL ISIL is empty): CommonStart_BeginLoadingCR, CreateNewPlayerSpawnData +
  SpawnPlayer per saved player, SpawnAreaCR, `XNetworkSYS.Deserialize_RejoinData`, room restore, PostFixupPlayers, ShowHud.
- Naming raw calls: `decomp/cs-addr` (Cpp2IL `--use-processor attributeinjector`) has `[Address(RVA=...)]` per method;
  VA = 0x180000000 + RVA, then `objdump -d -M intel --start-address=... GameAssembly.dll`. Attribute Length can be
  far too short for coroutines; use the next method's address as the end.

## Reconnect / unlocks
- Host log lines: "StartGameWaitForReconnects", "Host sending N rejoin datas after area loading" (rejoins are queued until area load).
- Rejoin failures show on the client, so get Bre's Player.log when one fails.
- Host pushes some rewards to clients: S2C_WeaponUnlock, S2C_EmberRewardResponse, S2C_StartEmberDustSequenceResponse,
  S2C_EveeAbilityUpgradeUnlockedSequence. Other unlocks are probably saved by the host only (unconfirmed).

## Nexus (pre-run) shop
- One shared shop: `XGameState.NexusShopData` (`NexusShopData_t { ShopState, List<NexusShopItem_t> ShopItems }`), synced to clients,
  so a host-only patch should be enough.
- `NexusShopItem_t { eItemType ItemType; int ItemDefId; ushort ItemNetId; int SlotIndex }`. `eItemType`: Weapon 1, Relic 4, Ability 9, EmberTreeUpgrade 12.
- Stock comes from `GameUtils.GenerateNexusShopItems_AUTH(World, SaveSlot, Rand, NumItems, OutItemList, FromReroll, ExcludeList)`:
  pool = host slot's unlocked items, filtered by DLC / `XRelicDef.IsInPool` / `RelicUtils.PassesAllMaxOwnedChecks`, shuffled,
  each pick gets `World.GetNextNetId()`. Spawned by `GameInst.SpawnNexusShopItems`; rerolls via `GameUtils.ReRollNexusShop`.
- `GameInst.GetNumberOfNexusShopItems` is a constant 4.
- Shop rules (from Kevin): both players share the same 4 slots, so 4 relics total per run. A bought slot stays empty;
  a reroll refills only the open slots.

### Favorite-relics mod (v0.1.0, verified in game 2026-11-09: stock 179,177,219,131; buy one + reroll gave 92,152,130)
- Ordered favorites queue (Kevin + Bre, 2026-11-08; they may trim it to 4 later):
  179 Absolute Ambition, 177 Pristine Band, 219 Goliath Wargear, 131 Call to Arms, 92 Gilded Remains,
  152 Titan's Wargear, 130 Supreme Ambition, 133 Spite Shield, 146 Mimic King's Consort, 220 Midas Mail.
  All 10 are unlocked in Kevin's slot 0.
- Behavior: first stock = first 4 favorites. Each reroll fills open slots with the next favorites in the queue (skip
  ones already on display or owned). Once the queue runs out, keep the game's own random picks. Reset the queue cursor
  on a fresh stock (`FromReroll == false`). Rerolled-away favorites don't come straight back.
- Callers: `GameInst.StartGameCR` (first stock, FromReroll false) and `PlayerUtils.ReRollNexusShop` (host only). The
  reroll counts the shop drops still on display, passes that as NumItems, the live `NexusShopData.ShopItems` as
  OutItemList and the old items as ExcludeList, then puts the old SlotIndex values back and sends NexusShopData to peers.
- The generator also mixes in skills (abilities) by `XRandom.Chance01`; the mod turns replaced slots into relics.
- Hook: Harmony postfix on `GameUtils.GenerateNexusShopItems_AUTH`. Overwrites ItemType = Relic and ItemDefId on the
  last NumItems entries, keeps ItemNetId (struct list: write back `list[i] = modified`). Each favorite is offered at
  most once per run (cursor only moves forward), so a bought favorite never comes back from the queue.
- Eligibility mirrors the generator: `XRelicDef.Get(id)`, `enabled`, `SaveSlot.IsItemUnlocked(def)`,
  `IsInPool(NexusShop 9, Nexus 6)`, `RelicUtils.PassesAllMaxOwnedChecks(def, counts, World.AllPlayers, null)` with
  counts from `GetRelicInstanceCounts(World, Nexus, ref counts)`. Pool check skips DLC (generator also calls HasDLC).
- Off in Daily Dungeon / Boss Rush (`World.IsDailyDungeon` / `IsBossRush`). Log line per stock/reroll in LogOutput.log.
- OPEN QUESTION (Kevin asking Bre): should the queue re-offer a relic one player already owns? They might want a second
  copy when both can own it. Until decided, follow the game's own max-owned rules only. Relevant `XRelicDef` fields:
  `CheckAllPlayers`, `MaxGloballyOwned`, `MaxPlayerOwned`.
- Config (BepInEx cfg): `Enabled`, `FavoriteRelics = 179,177,219,131,92,152,130,133,146,220` (ids; names optional later).
- Test checklist: first stock shows favorites 1-4 with normal prices; buy one, reroll, open slots get 5+; Bre's client
  sees the same items and can buy them; queue resets next run.
- Relic names: `data/relic-names.tsv` (131 relics), from the English table in resources.assets (`relic_name_<defId>`,
  find it via `\nui_common_quit;Quit\r\n`; rows end in CRLF). IDs match the values in the save's `relicUnlocks`.

## Online resume with a second player (v0.4.0, untested)
- First 2-player test of v0.3.2: the host loaded the save, then hung in `SpawnAreaCR` -> `NET_SyncStartAreaDataCR`. The client sat at countdown "1" because resume skips `StartGame_Internal`, which is what sends the start packet (and rolls `XNetworkSYS.GameId` at 0x128, and sets the lobby "in progress" on the web API).
- Host sync waits (`XNetworkSYS.TickWaitNetSyncFlag`, `NetWaitForAllLoadingCR`) only count `XNetPlayerData.IsConnected` (connectionState 0x4C == Connected 5). Players in other states are skipped.
- Mid-run join (`HandlePacket_C2S_JoinRequest`, if `SERVER_GameStarted` 0xAC): request GameId must equal host GameId, and the PlatformId must be in `allPlayerStates` (0x108). Then it sets the state to Reconnecting (2), or to ReconnectWaitingForNextArea (4) plus `PlayersWaitingForNextArea` while loading. It sends `NET_SendPlayerConnectionStateUpdate`, sets lastAckedSnapshotId (0x88) to -1, and calls `SendInitialRejoinData(peer, clientFrame, idx, inEnterTear)`.
- Client `Handle_S2C_InitialRejoinData` only checks IsClient, then runs `GameInst.RejoinGameCR`. No lobby-state guard, so the host can push it to a client still sitting in the lobby.
- The ClientFrame arg is the first UInt64 of the client's join request (`GameEngine.Instance.CurrentFrame` on the client). The client uses it to estimate latency.
- The main-menu Rejoin button shows for 10 minutes after a disconnect (`SavedPlayerSlot.LastConnectedHost*`, 0x70-0x90).
- v0.4.0 (`OnlineRejoinPushPatch`):
  - Before resume, marks lobby peers state 4 and sets SERVER_GameStarted and a new GameId.
  - Once resume finishes (the `GameUI.ShowHud` postfix, the last call in ResumeSavedStateCR), pushes rejoin data to each peer.
  - The client frame is captured in a JoinRequest prefix that reads ahead in the XByteReader (Position 0x18), then aged by host frames.
