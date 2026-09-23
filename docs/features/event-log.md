# Runtime Event Log

F3 opens or selects the read-only Logs tab in the full-window [DevTools shell](devtools.md) in both Debug and exported Release builds. It is available in the lobby, network arena and local practice. Escape and the shell's persistent Close button perform UI navigation only. Opening DevTools suppresses local driving, camera and item input independently of the Settings menu; simulation, remote players, networking and event collection continue.

The panel fills the shell's available content area and shows millisecond elapsed timestamps, category, Host/Local origin, sequence and a human-readable outcome. Timestamps are muted gray, categories blue, captured player names red, and normal messages light gray against the dark shell. Names are styled at their structured actor/target positions, so an identical word in a cause or message keeps normal styling. All categories are initially visible. The category selector filters the existing history. Follow latest tracks incoming events; disabling it freezes the displayed snapshot for scrolling and selection while collection continues. Changing categories while frozen filters that same snapshot. Re-enabling it displays the current retained buffer. Both the journal and frozen presentation retain at most 1,024 entries. Oldest journal entries are evicted first, independently of the frozen snapshot. The last session journal remains inspectable after leaving; entering a new session starts a new history.

## Ownership and ordering

`Core.Events.RuntimeEvent` carries stable category and kind, original timestamp, sequence, session player identities and sanitized names, actor/target, cause/context, exact numeric amount, health, life and tick where applicable. `EventStream` owns bounded retention and authoritative sequence deduplication. Its watermark survives row eviction. Local diagnostics have a separate sequence and explicit Local origin. Clock advancement is supplied by the runtime, never read from an engine or wall clock in Core. Host session time continues through lobby and arena generations; isolated practice uses simulation time. Local diagnostic elapsed time belongs to the receiving runtime and must not be compared as a synchronized clock with host time.

`LobbyAuthority` owns authoritative presence/grace/session events. Its journal is passed into the existing `HostVehicleSession` and its simulation, rather than creating another gameplay authority. Session player names are captured at publication so later removal cannot relabel history. `Simulation` records every positive committed `VehicleStepResult.DamageEvents` result, including several hits in the same fixed step, rather than reading only the snapshot's final damage field. Collision cooldown/manifold suppression stays in `VehicleHealth`; zero/rejected contacts produce no applied-damage events. Lethal clamping is reflected in the exact applied amount. Healing reports the actual clamped gain and repair source. Item use/impact outcomes are staged before health processing and published only after the complete world batch succeeds. Scored kills come from the existing match authority's committed score deltas. Restore installs state without replaying event outcomes.

`LobbyNetworkDriver` publishes ordered bounded batches over the existing reliable gateway, guarded by the existing logical session and recipient connection-generation envelope. Clients accept only the established host, reliable delivery and the current connection generation. A complete batch is validated before any event is accepted. They do not infer hits, kills or presence transitions from replicated snapshots. New and resumed transports start at their admission/reconnect event, without receiving earlier gameplay history; previously observed sequences remain rejected. The event stream is independent of arena snapshot timing and survives Return/Start and in-process reconnect. There is no distributed audit database or durable event replay after process restart.

## Integrated categories

| Category | Outcomes |
| --- | --- |
| Session | Authoritative create/join/leave/arena/return/close; local online lobby operations and results |
| Network | Disconnect, grace entry/expiry, rebind, local recovery progress/failure, bounded rejection summaries, configuration application |
| Match | Creation, Waiting/Countdown/Active/Finished transitions, winner when available |
| Lifecycle | Spawn/despawn, death, respawn waiting, respawn, authoritative scored kill attribution |
| Damage | Each committed hit, exact applied amount, attacker, target, Missile/map collision/vehicle collision/explosion cause, remaining/max HP |
| Healing | Actual repair amount and source, including Wrench |
| Item | Grant, pickup, pickup activation, use/impact, projectile expiry/removal, inventory removal at life boundaries |
| Developer | Give Item and Force Start results, changed setting keys/old/new values, configuration rejection, practice reset/blast |

Host migration starts an empty journal for the committed authority epoch; it does not replay checkpointed gameplay as new events. The replacement records the authority transition and shares that journal with its restored simulation. Clients reset the old epoch's sequence watermark, and replicated Event Log envelopes validate the current authority epoch as well as connection generation. Ordinary reconnect and Return/Start retain the existing epoch's journal. Event history is diagnostic and is not part of the gameplay checkpoint. Ordinary prediction corrections, position/speed/surface changes and physics callbacks are not journaled. Repeated packet rejection diagnostics are summarized at most once per second. Ongoing gameplay outcomes are not downsampled.

## Extension and security contract

Future meaningful actions, transitions and failures should emit structured outcomes at the existing owner's committed boundary. Future damage/healing mechanisms must supply a safe cause and exact applied result. Extend the damage cause mapping for new mechanics; never forward arbitrary provider errors, EOS identities, authentication tokens, access codes or credentials to event fields. Display names use the same sanitizer as the session roster. Rendering uses literal `AddText` with BBCode disabled and native color spans; event fields never become markup.

The [player activity feed](activity-feed.md) projects this same structured stream through its post-acceptance `Appended` notification, reusing journal deduplication without maintaining a second history. A Dead event carries `Scored kill` in its existing Context when the same committed match change emits a Kill; this lets projections suppress redundant death messages even across packet boundaries. Future player-facing categories extend that filter without separate connection tracking, kill detection or damage authority.

## Verification

Core tests cover ordering, timestamp stability, bounded eviction, replay watermarks, codec integrity, same-tick fractional/lethal damage, collision suppression, scored kill ordering, inventory removal, exact healing/use ordering and safe reconnect/grace transitions. Transport tests exercise the production driver's replication, host trust, delivery mode, connection generation and duplicate checks. `check-event-log.ps1 -GodotPath <console executable>` runs the production bootstrap, panel and two native UDP worlds through `scenes/verification/event_log_checks.tscn`. Add `-Visual -Resolution 640x360` to capture and check the small viewport. After exporting Release, supply `-ReleaseExecutable <exported executable>` to test the actual export; this uses the bootstrap's explicit `--event-log-check` verification entry because Release templates do not accept editor scene overrides. Existing item, match, lifecycle, lobby and reconnect harnesses remain the integration regression routes. Separate-PC EOS, interruption/restart and long-running sessions still require real-device testing.

[Feature index](README.md) · [Sessions](sessions.md) · [Vehicles](vehicles.md) · [Items](items.md) · [Reconnection](reconnection.md) · [Developer Options](developer-options.md)

Fresh arena bootstrap withholds journal delivery until activation and advances the initial stream watermark while pending. Only committed admission emits Joined and Spawned; checkpoint preparation does not replay earlier gameplay, and cancelled provisional participants emit no Joined/Disconnected presence pair. See [session admission](sessions.md#fresh-admission-during-an-arena).

Oil deployment uses the generic confirmed item use outcome. Each authoritative patch entry records an Oil triggered item event with deployer and target; remaining in a patch produces no per-frame event.
