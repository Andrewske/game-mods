"""Decode/encode Ember Knights' TSBinary save format (rog.dat).

Every value is a 1-byte TSType tag followed by its payload. Objects and lists
carry an int32 count (-1 means null). Object keys and strings are .NET
BinaryWriter strings (7-bit length prefix + UTF-8).

Decoded values are (type_name, payload) tuples so encode() can reproduce the
original bytes exactly. Object payloads are lists of (key, value) pairs to keep
field order.
"""

import struct
from typing import Any

# (type_name, payload); Object payloads are [(key, TSValue)], List payloads are [TSValue]
TSValue = tuple[str, Any]

TYPE_NAMES = {
    2: "Int8",
    3: "Int32",
    4: "Int16",
    5: "Int64",
    6: "IntPtr",
    7: "UInt8",
    8: "UInt32",
    9: "UInt16",
    10: "UInt64",
    11: "UIntPtr",
    12: "Float",
    13: "Double",
    14: "Bool",
    16: "String",
    17: "DateTime",
    18: "List",
    19: "Object",
}
TYPE_TAGS = {name: tag for tag, name in TYPE_NAMES.items()}

# struct formats for fixed-size scalars (DateTime is DateTime.ToBinary(), an int64)
SCALAR_FORMATS = {
    "Int8": "<b",
    "Int32": "<i",
    "Int16": "<h",
    "Int64": "<q",
    "IntPtr": "<q",
    "UInt8": "<B",
    "UInt32": "<I",
    "UInt16": "<H",
    "UInt64": "<Q",
    "UIntPtr": "<Q",
    "Float": "<f",
    "Double": "<d",
    "Bool": "<?",
    "DateTime": "<q",
}


class SaveFormatError(ValueError):
    pass


def _read_varint(data: bytes, pos: int) -> tuple[int, int]:
    result, shift = 0, 0
    while True:
        if pos >= len(data):
            raise SaveFormatError(f"varint runs past end of data at offset {pos}")
        byte = data[pos]
        result |= (byte & 0x7F) << shift
        pos += 1
        if not byte & 0x80:
            return result, pos
        shift += 7


def _read_string(data: bytes, pos: int) -> tuple[str, int]:
    length, pos = _read_varint(data, pos)
    end = pos + length
    if end > len(data):
        raise SaveFormatError(f"string of length {length} at offset {pos} runs past end of data")
    return data[pos:end].decode("utf-8"), end


def _read_count(data: bytes, pos: int) -> tuple[int, int]:
    return struct.unpack_from("<i", data, pos)[0], pos + 4


def _decode_object(data: bytes, pos: int) -> tuple[list[tuple[str, TSValue]] | None, int]:
    count, pos = _read_count(data, pos)
    if count < 0:
        return None, pos
    fields = []
    for _ in range(count):
        key, pos = _read_string(data, pos)
        value, pos = decode_value(data, pos)
        fields.append((key, value))
    return fields, pos


def _decode_list(data: bytes, pos: int) -> tuple[list[TSValue] | None, int]:
    count, pos = _read_count(data, pos)
    if count < 0:
        return None, pos
    items = []
    for _ in range(count):
        item, pos = decode_value(data, pos)
        items.append(item)
    return items, pos


def decode_value(data: bytes, pos: int = 0) -> tuple[TSValue, int]:
    """Decode one tagged value at pos. Returns ((type_name, payload), next_pos)."""
    tag = data[pos]
    name = TYPE_NAMES.get(tag)
    if name is None:
        raise SaveFormatError(f"unsupported TSType tag {tag} at offset {pos}")
    pos += 1
    if name == "Object":
        payload, pos = _decode_object(data, pos)
    elif name == "List":
        payload, pos = _decode_list(data, pos)
    elif name == "String":
        payload, pos = _read_string(data, pos)
    else:
        fmt = SCALAR_FORMATS[name]
        payload = struct.unpack_from(fmt, data, pos)[0]
        pos += struct.calcsize(fmt)
    return (name, payload), pos


def decode(data: bytes) -> TSValue:
    """Decode a whole save file. Fails if any bytes are left over."""
    value, pos = decode_value(data, 0)
    if pos != len(data):
        raise SaveFormatError(f"decoded {pos} of {len(data)} bytes; trailing data left")
    return value


def _encode_varint(n: int) -> bytes:
    out = bytearray()
    while True:
        byte = n & 0x7F
        n >>= 7
        out.append(byte | (0x80 if n else 0))
        if not n:
            return bytes(out)


def _encode_string(text: str) -> bytes:
    raw = text.encode("utf-8")
    return _encode_varint(len(raw)) + raw


def _encode_payload(name: str, payload: Any) -> bytes:
    if name == "Object":
        if payload is None:
            return struct.pack("<i", -1)
        body = b"".join(_encode_string(k) + encode(v) for k, v in payload)
        return struct.pack("<i", len(payload)) + body
    if name == "List":
        if payload is None:
            return struct.pack("<i", -1)
        return struct.pack("<i", len(payload)) + b"".join(encode(v) for v in payload)
    if name == "String":
        return _encode_string(payload)
    return struct.pack(SCALAR_FORMATS[name], payload)


def encode(value: TSValue) -> bytes:
    """Encode a (type_name, payload) tuple back to bytes."""
    name, payload = value
    if name not in TYPE_TAGS:
        raise SaveFormatError(f"cannot encode unknown type {name!r}")
    return bytes([TYPE_TAGS[name]]) + _encode_payload(name, payload)
