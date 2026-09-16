# Arena audio assets

The arena owns all playback. Main-menu audio is outside this package.

`sources.json` is the authoritative file manifest: original filenames, source URLs,
archive selections, SHA-256 checksums, project attachment IDs and derivative edits.
The three MP3s under `project/music` retain the original TS-35 attachment bytes and
names. They are project-provided assets; no external music license is asserted.

`kenney/impact`, `kenney/interface` and `kenney/scifi` contain only the selected
CC0 1.0 clips and their original `License.txt` notices. No attribution is required.
These are pack version 1.0 selections; archive hashes pin the acquired versions.

## Local Sonniss acquisition

Sonniss GDC 2026 recordings and all edited derivatives are **excluded from Git**.
The [bundle license](https://sonniss.com/gdc-bundle-license/) permits project/team
use and finished-game distribution, but restricts supplying loose sound files.
Do not force-add `assets/audio/sonniss` or publish the acquisition cache.

1. Download the official archives linked by the selected entries in `sources.json`
   from [Sonniss](https://gdc.sonniss.com/). The selection uses parts 1, 2 and 5.
2. Extract them into one local directory, preserving the source-pack subdirectories.
3. Install Python 3 and FFmpeg from their official distributions if unavailable.
4. Run from the repository root:

   ```powershell
   ./setup-audio.ps1 -SonnissDirectory 'D:/Audio/Sonniss2026' -PythonPath python -FfmpegPath ffmpeg
   ./import-godot.ps1 -GodotPath 'C:/path/to/Godot.NET.exe'
   ```

The importer verifies all original recording hashes before writing any output.
It uses only Python's standard library and an external FFmpeg executable; no audio
manager or runtime plugin is installed. Missing or altered sources fail clearly.
Run setup before launching arena scenes, native integration checks or exporting
the game. The menu does not need these local files. Finished game exports include
the imported assets in the game resource pack; do not ship a loose audio library.

## Editing and mixing

`tools/import-audio.py` owns deterministic processing. All derivatives use mono
44.1 kHz 16-bit PCM. The manifest pins source windows, pitch and loop flags.
Peaks normalize to 23000/32768; one-shots receive a 50 ms tail fade. Loops join
their tails to their heads with a 100 ms crossfade and rotate the seam into the
clip, avoiding a hard splice. Runtime engine mixing additionally smooths gains.

The Mustang slow-driving recording supplies idle/low/high presentation layers
from different windows with pitch treatment. These are designed engine layers,
not measured recordings of three calibrated RPM bands. The current vehicle model
has no transmission or RPM state; observed road speed controls their mix.
Gravel tire-skid recording supplies the skid layer on the prototype's surfaces.
The designed jet blast supplies missile fire. The Anime Game blast supplies
explosions. Trailer Boom 011 supplies destruction, lowered death and end stings.
Arena ambience blends 65% wind and 35% factory-loop material, then closes the seam.
The end sting mixes the heavy Kenney metal impact into its boom at half gain.
Heavy collisions and destruction also add a separate spatial metal impact at runtime.

FFmpeg and Python are development tools only and are not redistributed here.
Validation used FFmpeg from imageio-ffmpeg 0.6.0 and Python 3.12; that temporary
tool installation is outside tracked assets. The manifest records original-file
hashes rather than claiming bit-identical encoders across arbitrary FFmpeg versions.
