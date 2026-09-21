#!/usr/bin/env python3
"""Adds the ONNX metadata sherpa-onnx needs to a stock Piper voice, and writes its tokens.txt.

sherpa-onnx republishes most Piper voices with this metadata already baked in, so its own archives
need nothing.  de_DE-mls-medium is the exception we care about -- the only medium-quality German
voice with female speakers under a permissive licence (CC-BY 4.0, trained from scratch) -- and it is
not in the sherpa release, so it comes straight from rhasspy/piper-voices and arrives without it.
Loading it as-is fails with::

    offline-tts-vits-model.cc:Init:169 'sample_rate' does not exist in the metadata

Upstream's scripts/piper/add_meta_data.py does this with the `onnx` and `iso639` Python packages.
Requiring a ~100 MB pip install in the release build for nine key/value pairs is not a trade worth
making, so this writes the protobuf by hand.

That is safe because of two facts, both checked at run time:

*   ``metadata_props`` is field 14 of ``ModelProto``, a *repeated* field, and protobuf lets the
    fields of a message appear in any order -- so appending encoded entries to the end of the file
    is a valid encoding of the same message with those entries added.
*   A stock Piper voice carries no ``metadata_props`` at all, so there is nothing to clear and no
    chance of a duplicate key.  If that ever stops being true this script refuses rather than
    producing a model with two different sample rates in it.
"""

from __future__ import annotations

import json
import pathlib
import sys

# ModelProto.metadata_props = 14, wire type 2 (length-delimited).
_METADATA_PROPS_TAG = (14 << 3) | 2
# StringStringEntryProto.key = 1, .value = 2, both length-delimited.
_KEY_TAG = (1 << 3) | 2
_VALUE_TAG = (2 << 3) | 2


def _varint(n: int) -> bytes:
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | (0x80 if n else 0))
        if not n:
            return bytes(out)


def _delimited(tag: int, payload: bytes) -> bytes:
    return bytes([tag]) + _varint(len(payload)) + payload


def _entry(key: str, value: str) -> bytes:
    inner = _delimited(_KEY_TAG, key.encode("utf-8")) + _delimited(
        _VALUE_TAG, str(value).encode("utf-8")
    )
    return _delimited(_METADATA_PROPS_TAG, inner)


def write_tokens(config: dict, path: pathlib.Path) -> int:
    rows = []
    for symbol, ids in config["phoneme_id_map"].items():
        if symbol == "\n":
            continue
        rows.append((ids[0] if isinstance(ids, list) else ids, symbol))
    rows.sort()
    path.write_text("".join(f"{symbol} {i}\n" for i, symbol in rows), encoding="utf-8")
    return len(rows)


def metadata_for(config: dict, language: str) -> dict[str, object]:
    sample_rate = config["audio"]["sample_rate"]
    if sample_rate == 22500:  # upstream typo in some voice configs
        sample_rate = 22050
    voice = config.get("lang_code") or config["espeak"]["voice"]
    return {
        "model_type": "vits",
        "comment": "piper",  # sherpa-onnx keys its phonemization path off this exact value
        "language": language,
        "voice": voice,
        "version": 1,
        "has_espeak": 1,
        "has_g2pw": 0,
        "n_speakers": config["num_speakers"],
        "sample_rate": sample_rate,
    }


def main(argv: list[str]) -> int:
    if len(argv) != 3:
        print(f"usage: {argv[0]} <model.onnx> <language-name>", file=sys.stderr)
        return 2

    model = pathlib.Path(argv[1])
    config = json.loads(model.with_suffix(".onnx.json").read_text(encoding="utf-8"))

    raw = model.read_bytes()
    for key in metadata_for(config, argv[2]):
        # The exact bytes a StringStringEntryProto.key of this name encodes to.
        if _delimited(_KEY_TAG, key.encode("utf-8")) in raw:
            print(
                f"{model.name} already carries metadata_props ('{key}'); refusing to append "
                "a second copy -- delete it and re-download.",
                file=sys.stderr,
            )
            return 1

    appended = b"".join(_entry(k, v) for k, v in metadata_for(config, argv[2]).items())
    with model.open("ab") as f:
        f.write(appended)

    count = write_tokens(config, model.parent / "tokens.txt")
    meta = metadata_for(config, argv[2])
    print(
        f"  {model.name}: +{len(appended)} B metadata "
        f"({meta['sample_rate']} Hz, {meta['n_speakers']} speakers), "
        f"tokens.txt: {count} symbols",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
