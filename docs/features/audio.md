# Arena audio presentation

Practice and network arenas own an `ArenaAudio` node. It reconstructs sounds from
confirmed vehicle, item and match state. Core, network payloads and gameplay timing
contain no new audio concepts. Removing the arena frees all streams, emitters and
voices; the music completion callback is disconnected and playlist state resets.
Main-menu scenes, controls and audio are unchanged.

## Music and match lifecycle

Music starts only when the existing match phase is Active. `ArenaPlaylist` calls an
injected random selector once to choose index 0, 1 or 2, then advances in fixed
order on native stream completion: 1 → 2 → 3 → 1. Repeated active publications do
not restart or reshuffle playback. Leaving Active stops the player and resets the
index. A late completion after stopping cannot restart it. Each reconstructed
match has independent Client randomness; music is never replicated.

Late joins or resume checkpoints initialize current presentation without replaying
historical kills, pickups, repairs or match stings. An already-active resumed arena
keeps its current playlist position; a freshly reconstructed active arena starts
its own playlist. Practice has no authoritative match phase, so it plays vehicle
feedback and ambience but no battle playlist or invented match countdown.

Countdown beeps follow the existing Core deadline and latest received world tick.
The current remaining second plays once; delivery catch-up skips missed seconds.
Active transition plays match start. Finished transition plays match end and a
layered end sting. Local scored kills play confirmation. No unsupported timer,
menu navigation cue or gameplay state is introduced.

## Vehicles, combat and items

Each vehicle has spatial idle, low, high and skid loops. Idle/low/high use
equal-power crossfades with full low at 11.2 m/s and full high at 28 m/s; reverse
uses speed magnitude. Gain and pitch follow an exponential presentation smoother.
Dead/waiting vehicles fade to silence. Skid follows the existing grounded/sliding
diagnostic and speed. The prototype has no RPM or transmission authority; these
are presentation layers derived from road speed.

Confirmed collision damage selects normal or heavy metal impact (20 HP threshold),
plus a distinct damage hit for nonlethal loss. Damage sequences are consumed once
per vehicle life; a 120 ms gate consumes suppressed events so they cannot replay
later. Lethal lifecycle feedback is independent of that cooldown and gives one
destruction per life; the local victim also receives a death sting. New life
boundaries produce respawn feedback. First-observed state and reconnect reseeding
are silent for historical one-shots. Movement prediction never emits damage cues.

Reliable item publications produce missile fire, spatial travel loops, metal impact
and explosion; Wrench consumption produces repair feedback even at full health.
New grant tokens distinguish weapon and Wrench pickups, including same-item
reacquisition. Initial ownership is seeded silently. Publication revision and a
bounded 256-outcome identity window prevent repeated launch/repair/impact cues.
Projectile loops follow authoritative positions and stop when projectiles disappear.
The existing local practice detonation also plays one explosion at its blast origin.

There are at most eight vehicle emitters, sixteen projectile loops and forty
concurrent one-shot voices per arena. Finished voices retire immediately; new
voices beyond the cap are dropped. The old procedural item/vehicle sounds are
removed while their existing visual feedback remains.

## Buses and settings

```text
Master
├── Music
└── SFX
    ├── Vehicle
    ├── Weapons
    └── UI
```

The existing `PlayerSettingsController` remains the sole volume preference owner.
Master, Music and SFX use their existing saved linear gains and mute behavior.
Hierarchy setup creates missing buses without overwriting saved gains. Vehicle
contains engines/skid/collision/damage/destruction; Weapons contains missile
fire/travel/impact/explosion. UI contains pickups, repair, personal feedback and
match cues. Ambience feeds SFX directly. Personal death/kill and match cues are 2D;
world feedback and pickups are spatial. There is no parallel volume service.

## Assets and verification

[Asset instructions](../../assets/audio/README.md) and the [manifest](../../assets/audio/sources.json)
own acquisition, selected files, licenses and processing. The three project songs
are committed unchanged. Sonniss derivatives must be imported locally before
arena startup or export; they are intentionally absent from the public repository.

Pure Client NUnit tests cover routing, gain bounds, event cooldowns, engine
continuity, playlist start/advance/wrap/reset, duplicate damage/lifecycle/item
events, initial state and reconnect reseeding, and countdown/match transitions.
`check-audio.ps1 -GodotPath <exe>` loads every stream, checks native bus routing and
settings, seeks actual MP3s near their ends to exercise completion/advancement,
and verifies music teardown. The eight-peer match harness also checks music on
Active, stopped/reset playlists on Finished, and submitted audio feedback per peer.
These checks establish native playback behavior, not subjective listening quality
or separate-machine EOS acoustics.

Related systems: [vehicles](vehicles.md), [items](items.md), [pickups](item-spawns.md),
[death/respawn](death-respawn.md), [matches](matches.md), [settings](settings.md),
[reconnection](reconnection.md).

[Feature index](README.md)
