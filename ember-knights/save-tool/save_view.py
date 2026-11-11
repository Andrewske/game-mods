"""Readable views of an Ember Knights save (rog.dat).

Usage:
  uv run save_view.py summary <rog.dat>                 unlock counts per save slot
  uv run save_view.py dump <rog.dat> [--slot N] [--category NAME]
                                                        JSON of one slot (or the whole file)

Always run this on a copy of the save, never the live file.
"""

import argparse
import json
import sys
from pathlib import Path
from typing import Any

from ts_binary import TSValue, decode

REDACTED_KEYS = frozenset({"StreamOauthToken"})

# Slot keys grouped by what they mean for unlock syncing. Anything missing lands in "other".
CATEGORIES: dict[str, frozenset[str]] = {
    "unlock": frozenset(
        {
            "weaponUnlocks",
            "abilityUnlocks",
            "relicUnlocks",
            "UnlockedAbilityUpgrades",
            "EmberTreeUnlockData",
            "weaponModData",
            "EmberDusts",
            "ExtraUnlockedWeaponSkinIds",
            "CleansedRelics",
            "UnlockedEmberDog",
            "WeaponChallengeList",
            "ChallengeList",
            "AchievementList",
            "GameMods",
            "SecretData",
            "PurchasedTokenShopIds",
        }
    ),
    "story": frozenset(
        {
            "bossKillData",
            "areaData",
            "npcData",
            "MarvinState",
            "YaleeState",
            "CollectorState",
            "BobJarState",
            "FishingStewardState",
            "AbilityUpgradeUnlockState",
            "MontyChestLevel",
            "MontyChestMaxLevel",
            "MontyChestFinishedAllLevels",
            "MontyKingBattleIsWon",
            "PraxisGenericDialogChance01",
            "PraxisNextEncounter",
            "ArchitectRealmEnemyPoolNumber",
            "ArchitectDefeatCount",
            "HasSeenArchitectDeath",
            "HasSeenTruePraxisDeath",
            "HasSeenRegularPraxisDeath",
            "HasSeenTrueArchitectDeath",
            "DarkCapDefeatedPraxis",
            "WatchedCinematics",
            "CutscenesViewed",
            "WatchedIntroCinematic",
            "SeenChaosMod",
            "SeenTrinityMod",
            "CompletedTrinity",
            "SeenOfferingPit",
            "CompletedBossRushIds",
        }
    ),
    "currency": frozenset({"numShards", "shardsSpent", "TotalEmberCollected", "ModEnergy", "TotalWellGold", "Tokens"}),
    "stats": frozenset(
        {
            "numRuns",
            "totalPlayTime",
            "GameOverCount",
            "NumVictories",
            "FastestClearTime",
            "NumGoldSpentInShops",
            "MostRoomsClearedInRun",
            "LongestRunTime",
            "itemCollectData",
            "enemyKillData",
            "MinibossEncounters",
            "BossEncounters",
            "PreviousRuns",
            "RunCounter",
            "WeaponData",
            "CreditsHighestScore",
            "CatPropsHit",
            "PlayerCatPoints",
            "GotCatEmberDustReward",
            "GotCatEarReward",
            "LastCatChallengeEmberRewardPoints",
            "DailyDungeon",
        }
    ),
    "telemetry": frozenset(
        {
            "SP_DF_LastSpawnedSkillDefId",
            "SP_DF_LastSpawendRelicDefId",
            "CheckSecretsAchievementsOnStartup",
            "RelicEnemyKillCountRequiredIndex",
            "RelicCurrentEnemyKillCount",
            "DLCRelicEnemyKillCountRequiredIndex",
            "DLCRelicCurrentEnemyKillCount",
        }
    ),
    "session": frozenset(
        {
            "LastConnectedLobbyId",
            "LastConnectedHost",
            "LastConnectedHostTimestamp",
            "LastConnectedHostGameId",
            "LastConnectedChallengeDataString",
            "SelectedDifficulty",
            "lastWeaponDefId",
            "EquippedWeaponsMask",
            "LastEquippedColorId",
            "LastEquippedColorInfo",
            "SavedColorPresets",
            "EmberTreeColourDefId",
            "SeenButtonTips",
            "UsedRelicSelector",
            "UsedSkillSelector",
            "UsedCompendium",
            "newAbilities",
            "newRelics",
            "unknownRelics",
            "abilityTablets",
            "SavedState",
            "MultiplayerSavedState",
            "PreviousArchitectFirstArea",
        }
    ),
}


def category_of(key: str) -> str:
    if key.startswith("METRIC_"):
        return "telemetry"
    return next((name for name, keys in CATEGORIES.items() if key in keys), "other")


def to_plain(value: TSValue) -> Any:
    """Strip type tags so the value serializes as ordinary JSON."""
    name, payload = value
    if payload is None:
        return None
    if name == "Object":
        return {k: "<redacted>" if k in REDACTED_KEYS else to_plain(v) for k, v in payload}
    if name == "List":
        return [to_plain(v) for v in payload]
    return payload


def get_slots(save: TSValue) -> list[dict[str, TSValue] | None]:
    """Return each save slot as a key -> value dict (None for an empty slot)."""
    slots = dict(save[1])["Slots"][1]
    return [None if s[1] is None else dict(s[1]) for s in slots]


def _count_flag(items: list[dict], flag: str) -> int:
    return sum(1 for item in items if item.get(flag))


def summarize_slot(slot: dict[str, TSValue]) -> dict[str, Any]:
    p = {k: to_plain(v) for k, v in slot.items()}
    return {
        "runs": p["numRuns"],
        "victories": p["NumVictories"],
        "weapons_unlocked": len(p["weaponUnlocks"]),
        "skills_unlocked": len(p["abilityUnlocks"]),
        "relics_unlocked": len(p["relicUnlocks"]),
        "skill_upgrades_unlocked": len(p["UnlockedAbilityUpgrades"]),
        "weapon_mods_unlocked": _count_flag(p["weaponModData"], "Unlocked"),
        "ember_tree_unlocked": _count_flag(p["EmberTreeUnlockData"], "Unlocked"),
        "ember_tree_levels": sum(e["Level"] for e in p["EmberTreeUnlockData"]),
        "ember_dusts": len(p["EmberDusts"]),
        "bosses_defeated": _count_flag(p["bossKillData"], "IsDefeated"),
        "weapon_challenges_done": _count_flag(p["WeaponChallengeList"], "Completed"),
        "challenges_done": _count_flag(p["ChallengeList"], "Completed"),
        "secrets_found": len(p["SecretData"]),
        "embers": p["numShards"],
    }


def slot_by_category(slot: dict[str, TSValue], category: str | None) -> dict[str, Any]:
    return {k: to_plain(v) for k, v in slot.items() if category is None or category_of(k) == category}


def load_save(path: Path) -> TSValue:
    return decode(path.read_bytes())


def cmd_summary(args: argparse.Namespace) -> Any:
    slots = get_slots(load_save(args.save))
    return {f"slot{i}": summarize_slot(s) if s else None for i, s in enumerate(slots)}


def cmd_dump(args: argparse.Namespace) -> Any:
    save = load_save(args.save)
    if args.slot is None:
        return to_plain(save)
    slot = get_slots(save)[args.slot]
    if slot is None:
        raise SystemExit(f"save_view: slot {args.slot} is empty in {args.save}")
    return slot_by_category(slot, args.category)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Inspect an Ember Knights rog.dat save")
    sub = parser.add_subparsers(dest="command", required=True)
    summary = sub.add_parser("summary", help="unlock counts per slot")
    summary.add_argument("save", type=Path)
    summary.set_defaults(run=cmd_summary)
    dump = sub.add_parser("dump", help="JSON dump")
    dump.add_argument("save", type=Path)
    dump.add_argument("--slot", type=int)
    dump.add_argument("--category", choices=[*CATEGORIES, "other"])
    dump.set_defaults(run=cmd_dump)
    return parser


def main(argv: list[str] | None = None) -> None:
    args = build_parser().parse_args(argv)
    json.dump(args.run(args), sys.stdout, indent=2)
    print()


if __name__ == "__main__":
    main()
