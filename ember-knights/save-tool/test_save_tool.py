"""Run with: uv run --no-project --with pytest pytest -q"""

import json

import pytest

from save_view import category_of, get_slots, main, summarize_slot, to_plain
from ts_binary import SaveFormatError, decode, decode_value, encode


def obj(*fields):
    return ("Object", list(fields))


def lst(*items):
    return ("List", list(items))


def make_slot(**overrides):
    base = {
        "numRuns": ("Int32", 3),
        "NumVictories": ("Int32", 1),
        "weaponUnlocks": lst(("Int32", 1), ("Int32", 2)),
        "abilityUnlocks": lst(("Int32", 5)),
        "relicUnlocks": lst(),
        "UnlockedAbilityUpgrades": lst(),
        "weaponModData": lst(obj(("Unlocked", ("Bool", True))), obj(("Unlocked", ("Bool", False)))),
        "EmberTreeUnlockData": lst(obj(("Level", ("Int32", 2)), ("Unlocked", ("Bool", True)))),
        "EmberDusts": lst(),
        "bossKillData": lst(obj(("IsDefeated", ("Bool", True)))),
        "WeaponChallengeList": lst(),
        "ChallengeList": lst(obj(("Completed", ("Bool", True)))),
        "SecretData": lst(),
        "numShards": ("Int32", 500),
    }
    return obj(*{**base, **overrides}.items())


def make_save(*slots):
    return obj(("SaveVersion", ("Int32", 59)), ("Slots", lst(*slots)))


def test_roundtrip_every_scalar_type():
    value = obj(
        ("i8", ("Int8", -5)),
        ("i16", ("Int16", -300)),
        ("i32", ("Int32", -70000)),
        ("i64", ("Int64", -(2**40))),
        ("u8", ("UInt8", 200)),
        ("u16", ("UInt16", 60000)),
        ("u32", ("UInt32", 4_000_000_000)),
        ("u64", ("UInt64", 2**63)),
        ("f", ("Float", 1.5)),
        ("d", ("Double", 2.25)),
        ("b", ("Bool", True)),
        ("s", ("String", "héllo")),
        ("dt", ("DateTime", 638_000_000_000_000_000)),
    )
    assert decode(encode(value)) == value


def test_roundtrip_nested_and_null_containers():
    value = obj(
        ("empty", obj()),
        ("nullobj", ("Object", None)),
        ("nulllist", ("List", None)),
        ("nested", lst(obj(("x", lst(("Int32", 1)))))),
    )
    assert decode(encode(value)) == value


def test_long_string_uses_multibyte_length_prefix():
    text = "a" * 300
    encoded = encode(("String", text))
    assert encoded[1:3] == bytes([0xAC, 0x02])  # 300 as a 7-bit varint
    assert decode(encoded) == ("String", text)


def test_known_bytes_from_real_save_header():
    raw = bytes.fromhex("13 01000000 0b") + b"SaveVersion" + bytes.fromhex("03 3b000000")
    assert decode(raw) == obj(("SaveVersion", ("Int32", 59)))


def test_trailing_bytes_rejected():
    with pytest.raises(SaveFormatError, match="trailing"):
        decode(encode(("Int32", 1)) + b"\x00")


def test_unknown_tag_rejected():
    with pytest.raises(SaveFormatError, match="tag 1 at offset 0"):
        decode_value(b"\x01\x00")


def test_truncated_string_rejected():
    with pytest.raises(SaveFormatError, match="runs past end"):
        decode(bytes([16, 10]) + b"abc")


def test_to_plain_strips_tags_and_redacts_token():
    value = obj(("a", lst(("Int32", 1))), ("StreamOauthToken", ("String", "secret")))
    assert to_plain(value) == {"a": [1], "StreamOauthToken": "<redacted>"}


@pytest.mark.parametrize(
    ("key", "expected"),
    [
        ("relicUnlocks", "unlock"),
        ("bossKillData", "story"),
        ("numShards", "currency"),
        ("PreviousRuns", "stats"),
        ("METRIC_Anything", "telemetry"),
        ("SavedState", "session"),
        ("BrandNewKey", "other"),
    ],
)
def test_category_of(key, expected):
    assert category_of(key) == expected


def test_get_slots_handles_empty_slot():
    slots = get_slots(make_save(make_slot(), ("Object", None)))
    assert slots[1] is None
    assert to_plain(slots[0]["numRuns"]) == 3


def test_summarize_slot_counts_flags():
    summary = summarize_slot(dict(make_slot()[1]))
    assert summary["weapons_unlocked"] == 2
    assert summary["weapon_mods_unlocked"] == 1
    assert summary["ember_tree_levels"] == 2
    assert summary["bosses_defeated"] == 1
    assert summary["challenges_done"] == 1


def test_cli_summary_and_dump(tmp_path, capsys):
    save = tmp_path / "rog.dat"
    save.write_bytes(encode(make_save(make_slot(), ("Object", None))))
    main(["summary", str(save)])
    assert json.loads(capsys.readouterr().out)["slot1"] is None
    main(["dump", str(save), "--slot", "0", "--category", "currency"])
    assert json.loads(capsys.readouterr().out) == {"numShards": 500}
