# CC0 audio placeholder replacement verification

Verified 2026-09-17 UTC on the user-assigned current branch. This is an independent
assignment with no active Jira key; TS-63 was not reopened or modified.

## Scope and delivered assets

The ten runtime WAVs under `assets/audio/freesound` replace the prior local audio
package. [The audio manifest](../../assets/audio/sources.json) records the nine
exact approved Freesound IDs, authors, original titles, official preview/download
URLs, CC0 verification, acquired preview hashes, derivative hashes and processing.
[The asset guide](../../assets/audio/README.md) lists every final path and edit.

Acquired IDs: 401552, 401556, 401551 (GiocoSound engine set), 529225
(UnplugTheFridge skid), 398213 (morganpurkis missile), 811927 (claywh explosion),
816376 (harrisonlace boom), 790753 (JWS24 factory), 423314 (haniebal wind).
Original downloads required login; official high-quality MP3 previews of those
same recordings were acquired instead. No different recording or non-CC0 source
was substituted. Their decoded derivatives total 3,272,130 bytes, stored directly
in Git without LFS. No source-acquisition cache is committed.

AudioCatalog changes only the ten resource paths and its summary. All event,
spatialization, bus, settings, music, engine mixing and multiplayer logic remains
unchanged. The optional `tools/process-cc0-audio.py` replaces the old importer;
`setup-audio.ps1` and the obsolete ignore rule are removed. Current provenance and
feature documentation are synchronized. Historical TS-34 evidence is preserved.
The user's pre-existing `export_presets.cfg` edit is outside this change.

## Verified

- `check.ps1`: restore and formatting pass; Debug and Release builds report zero
  warnings/errors. Each configuration passes 310 Core and 150 non-native
  Client/transport tests.
- Normal Godot 4.7.2 .NET import passes without warnings/errors.
- `check-audio.ps1 -NoBuild`: 53 native assertions pass, covering all imported
  streams, buses, settings, playlist completion and arena lifecycle, including
  the existing local UDP peer integration.
- All manifest file hashes match. Existing Kenney and music Git blobs retain their
  recorded hashes. All ten new files are mono 44.1 kHz 16-bit PCM; sample peaks
  are at or below 23000. One-shot silence was trimmed and loop boundaries were
  crossfaded. Numeric inspection is not a subjective listening judgment.
- The new preflight rejects both missing assets and checksum mismatches before
  starting Godot, verified with isolated temporary fixtures.
- An actual fresh local clone of implementation commit `76c7f6a` contains all ten
  tracked derivatives and no `assets/audio/sonniss` directory. With no copied audio
  or import cache, normal Godot import passes. After providing the existing pinned
  EOS SDK prerequisite, the clone's Debug build passes with zero warnings/errors,
  its audio harness passes 53 assertions, and local practice starts/exits normally
  (`--headless --quit-after 120 -- --local-practice`) without runtime diagnostics.
- Current tracked code/docs contain no obsolete source/setup references outside
  historical verification evidence. Diff whitespace checks pass. No Core or
  dependency-direction changes and no Shared layer were introduced.

## Limits and interpretation

The first clean-clone build correctly failed because the existing EOS SDK was
absent. The documented `setup-eos.ps1 -ArchivePath <existing pinned archive>` was
then run in the clone. No audio acquisition or audio processing was run there.
Thus all required audio is self-contained in a clean checkout, but a literal bare
clone/import/build/run still has the pre-existing EOS SDK prerequisite. Removing
that separate prerequisite is outside the audio assignment.

Subjective listening/gameplay sound quality and separate-machine EOS acoustics
are UNVERIFIED. Native playback, resource availability and lifecycle behavior are
VERIFIED; preservation of unchanged routing/event behavior is additionally
supported by source inspection and the existing automated tests.
