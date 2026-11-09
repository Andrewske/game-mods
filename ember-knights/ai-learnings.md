# AI Learnings: Ember Knights mods

## Setup
- Game: Unity 2022.3.62f3, IL2CPP, metadata v31, Steam app 1135230, runs under Proton on Kevin's PC.
- Install: `~/.local/share/Steam/steamapps/common/EmberKnights` (Proton sees it as `S:\steamapps\common\EmberKnights`).
- Save: `EmberKnights_64_Data/SaveData/rog.dat` (unencrypted typed key/value binary; auto backups in `SaveData/Backups/`).
  Steam Cloud is on, so edit saves with the game closed and back them up first.
- Logs: `compatdata/1135230/pfx/drive_c/users/steamuser/AppData/LocalLow/Doom Turtle/EmberKnights/Player.log` (+ `Player-prev.log`).
- Netcode: Photon (`XNetworkPhoton`) with the game's own packet layer in `XNetworkSYS` (C2S_/S2C_ handlers). Kevin hosts, Bre joins online.
- Loader plan: BepInEx 6 IL2CPP, same as ../ship-of-fools (not installed yet). Stable BepInEx rejects metadata v31
  ("We support 23-29, got 31"); bleeding-edge builds support it since mid-2025 (BepInEx issues #1115, #1120), so be.789
  should work. Unconfirmed until we launch it.
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

### Favorite-relics mod plan (not built; test after the current run ends)
- Ordered favorites queue (Kevin + Bre, 2026-11-08; they may trim it to 4 later):
  179 Absolute Ambition, 177 Pristine Band, 219 Goliath Wargear, 131 Call to Arms, 92 Gilded Remains,
  152 Titan's Wargear, 130 Supreme Ambition, 133 Spite Shield, 146 Mimic King's Consort, 220 Midas Mail.
  All 10 are unlocked in Kevin's slot 0.
- Behavior: first stock = first 4 favorites. Each reroll fills open slots with the next favorites in the queue (skip
  ones already on display or owned). Once the queue runs out, keep the game's own random picks. Reset the queue cursor
  on a fresh stock (`FromReroll == false`). Rerolled-away favorites don't come straight back.
- Hook: Harmony postfix on `GameUtils.GenerateNexusShopItems_AUTH`. Overwrite ItemType = Relic (4) and ItemDefId in
  `OutItemList`, keep ItemNetId and SlotIndex (struct list: write back `list[i] = modified`). NumItems on a reroll should
  equal the open-slot count; confirm in game.
- Skip a favorite that fails `RelicUtils.PassesAllMaxOwnedChecks` / `XRelicDef.IsInPool` (e.g. Ambition or Wargear pairs
  could be mutually exclusive; unverified). Disable in Daily Dungeon / Boss Rush.
- They buy one copy of each, and some of these relics are one-per-team (they help both knights). So skip a favorite if
  EITHER player already owns it, not just the buyer. Relevant `XRelicDef` fields: `CheckAllPlayers`, `MaxGloballyOwned`,
  `MaxPlayerOwned`; check all players' inventories ourselves rather than trusting the per-player check.
- Config (BepInEx cfg): `Enabled`, `FavoriteRelics = 179,177,219,131,92,152,130,133,146,220` (ids; names optional later).
- Test checklist: first stock shows favorites 1-4 with normal prices; buy one, reroll, open slots get 5+; Bre's client
  sees the same items and can buy them; queue resets next run.
- Relic names: `data/relic-names.tsv` (131 relics), from the English table in resources.assets (`relic_name_<defId>`,
  find it via `\nui_common_quit;Quit\r\n`; rows end in CRLF). IDs match the values in the save's `relicUnlocks`.
