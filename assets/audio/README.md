# Arena audio assets

Arena playback and the MenuShell's independent Music-bus player share this package.
Every required runtime clip is committed: clone, perform the normal Godot import,
build and run. No audio download, Python, FFmpeg or manual audio setup is required.
The normal project prerequisites still apply, including the separately documented
[EOS SDK setup](../../docs/eos-development.md); this change removes audio setup only.

`sources.json` is the authoritative manifest of source identities, licenses,
acquired-byte and derivative SHA-256 hashes, and processing selections. The three
arena MP3s under `project/music` retain the original TS-35 attachment bytes and names.
They are project-provided assets; no external music license is asserted.
The fourth MP3 is the unchanged TS-85 authoritative frontend track; its paired video,
provenance and conversion record are in the [frontend media record](../frontend/README.md).

`kenney/impact`, `kenney/interface` and `kenney/scifi` contain selected CC0 1.0
clips and original `License.txt` notices. These are pack version 1.0 selections;
archive hashes pin the acquired versions.

## Freesound CC0 placeholders

All files below are third-party CC0-1.0 derivatives, not Trackstorm-owned sounds.
They may be committed publicly, redistributed with source, modified and used
commercially without attribution. Source links and authors are retained for
provenance. The [CC0 dedication](https://creativecommons.org/publicdomain/zero/1.0/)
is linked by each selected Freesound page and recorded in the manifest.

| Runtime path under `freesound/` | Freesound ID / author | Processing |
| --- | --- | --- |
| `vehicle/idle.wav` | [401552 / GiocoSound](https://freesound.org/people/GiocoSound/sounds/401552/) | First 3.1 s; loop |
| `vehicle/low.wav` | [401556 / GiocoSound](https://freesound.org/people/GiocoSound/sounds/401556/) | First 2.1 s; loop |
| `vehicle/high.wav` | [401551 / GiocoSound](https://freesound.org/people/GiocoSound/sounds/401551/) | First 2.03 s; loop |
| `vehicle/skid.wav` | [529225 / UnplugTheFridge](https://freesound.org/people/UnplugTheFridge/sounds/529225/) | 5–9 s gravel section; loop |
| `combat/fire.wav` | [398213 / morganpurkis](https://freesound.org/people/morganpurkis/sounds/398213/) | First 1 s; discard silent tail |
| `combat/explosion.wav` | [811927 / claywh](https://freesound.org/people/claywh/sounds/811927/) | 0.18–2.38 s; remove lead-in and quiet tail |
| `combat/destruction.wav` | [816376 / harrisonlace](https://freesound.org/people/harrisonlace/sounds/816376/) | First 3.5 s; original pitch |
| `combat/death.wav` | 816376 / harrisonlace | First 3 s; pitch/rate ×0.7 (about 4.29 s output) |
| `combat/end.wav` | 816376 / harrisonlace + existing Kenney heavy metal impact | First 4 s; pitch/rate ×1.15; add metal at half gain |
| `arena/ambience.wav` | [423314 / haniebal](https://freesound.org/people/haniebal/sounds/423314/) + [790753 / JWS24](https://freesound.org/people/JWS24/sounds/790753/) | Wind 48–60 s and factory 8–20 s; normalize separately, loop, mix 65%/35%, normalize and close seam |

The three engine recordings are the same BMW 120d set. Runtime road-speed
crossfades and pitch smoothing remain unchanged; there is no new RPM authority.

Original downloads require Freesound login. These exact recordings were acquired
from Freesound's official high-quality MP3 previews, not the low-quality player
previews. No alternate recordings were substituted. The manifest distinguishes
preview hashes/URLs from original download links; it does not claim original WAV
bytes were acquired. The runtime WAVs preserve decoded preview quality without
further lossy compression. Only the game-ready derivatives are committed.

## Optional authoring

`tools/process-cc0-audio.py` reproduces the derivatives from the checksummed preview
files named `<id>.mp3` in an external source directory. This is an optional editing
tool, not an import prerequisite. It verifies all source hashes before processing:

```powershell
python tools/process-cc0-audio.py --sources 'D:/Audio/TrackstormCC0' --ffmpeg 'C:/tools/ffmpeg.exe'
```

All outputs are mono 44.1 kHz 16-bit PCM WAV. Peaks normalize to 23000/32768
(about -3.1 dBFS). One-shots receive 2 ms attack and 50 ms tail fades. Loops
crossfade the tail into the head over 100 ms and rotate the seam into the clip;
each loop pass shortens the selected window by 100 ms. The end sting retains the
half-gain Kenney metal layer; heavy collisions and destruction also retain their
separate spatial metal impact at runtime.

Python and FFmpeg are external authoring tools only, not redistributed dependencies.
The recorded processing used FFmpeg 7.1 from imageio-ffmpeg 0.6.0. Regeneration updates
derivative hashes; arbitrary decoder versions need not produce identical bytes.
WAV and MP3 files use ordinary Git binary storage. The larger TS-85 source and
runtime video files use the repository's scoped Git LFS rules.

`check-audio.ps1 -GodotPath <exe>` checks every manifest file's existence and hash,
then exercises imported streams, buses, settings, playlists and arena lifecycle.
