"""Optional authoring tool for committed CC0 placeholders; never needed to build/run."""
import argparse
import array
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import wave


def decode(ffmpeg, source, start, duration, pitch):
    result = subprocess.run(
        [ffmpeg, "-v", "error", "-ss", str(start), "-t", str(duration),
         "-i", str(source), "-af", f"aresample=44100,asetrate=44100*{pitch},aresample=44100",
         "-ac", "1", "-f", "s16le", "-"], capture_output=True, check=True)
    samples = array.array("h", result.stdout)
    if sys.byteorder != "little":
        samples.byteswap()
    return list(samples)


def normalize(samples, peak=23000):
    scale = peak / max(1, max(abs(s) for s in samples))
    return [int(s * scale) for s in samples]


def loop(samples):
    # Join the tail to the head, then rotate the seam into the clip.
    count = min(4410, len(samples) // 4)
    seam = [int(samples[-count + i] * (1 - i / count) + samples[i] * i / count)
            for i in range(count)]
    return samples[count:-count] + seam


def save(path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    data = array.array("h", samples)
    if sys.byteorder != "little":
        data.byteswap()
    with wave.open(str(path), "wb") as output:
        output.setparams((1, 2, 44100, 0, "NONE", "not compressed"))
        output.writeframes(data.tobytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sources", type=Path, required=True)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / "assets/audio/sources.json").read_text())
    source_root = args.sources.resolve()
    selection = manifest["freesound"]
    sources = {entry["id"]: entry for entry in selection["sources"]}
    for entry in sources.values():
        source = source_root / entry["file"]
        if hashlib.sha256(source.read_bytes()).hexdigest() != entry["sha256"]:
            raise ValueError(f"Source checksum mismatch: {source.name}")
    target = root / "assets/audio/freesound"
    with tempfile.TemporaryDirectory() as temporary:
        staging = Path(temporary)
        processed = {}
        for edit in selection["edits"]:
            samples = normalize(decode(args.ffmpeg, source_root / sources[edit["sourceId"]]["file"],
                                       edit["start"], edit["duration"], edit["pitch"]))
            if edit["loop"]:
                samples = loop(samples)
            else:
                attack = min(88, len(samples) // 4)
                for i in range(attack):
                    samples[i] = int(samples[i] * i / attack)
                fade = min(2205, len(samples) // 4)
                for i in range(fade):
                    samples[-fade + i] = int(samples[-fade + i] * (1 - i / fade))
            processed[edit["output"]] = samples
        wind, factory = processed.pop("arena/wind.wav"), processed.pop("arena/industrial.wav")
        count = min(len(wind), len(factory))
        processed["arena/ambience.wav"] = loop(normalize(
            [int(wind[i] * 0.65 + factory[i] * 0.35) for i in range(count)]))
        # End-match metal is mixed into the local UI sting, so its position cannot attenuate it.
        metal = decode(args.ffmpeg, root / "assets/audio/kenney/impact/impactMetal_heavy_000.ogg", 0, 2, 1)
        end = processed["combat/end.wav"]
        processed["combat/end.wav"] = normalize(
            [s + (int(metal[i] * 0.5) if i < len(metal) else 0) for i, s in enumerate(end)])
        for name, samples in processed.items():
            save(staging / name, samples)
        for name in processed:
            output = target / name
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes((staging / name).read_bytes())
            relative = output.relative_to(root).as_posix()
            manifest["files"] = [entry for entry in manifest["files"] if entry["path"] != relative]
            ids = [423314, 790753] if name == "arena/ambience.wav" else [
                edit["sourceId"] for edit in selection["edits"] if edit["output"] == name]
            manifest["files"].append({
                "path": relative, "sourceIds": ids, "license": "CC0-1.0",
                "sha256": hashlib.sha256(output.read_bytes()).hexdigest(),
                "processing": "freesound.edits and freesound.processing in this manifest",
            })
            print(f"Processed {relative}")
        (root / "assets/audio/sources.json").write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    main()
