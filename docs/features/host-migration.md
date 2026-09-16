# Host migration and match rehosting

## Authority and identity

Trackstorm owns the logical session, stable player IDs, `CurrentHostId`, `AuthorityEpoch`, and match generation. The initial host has player ID 1; later hosts keep their existing player IDs. A successful transfer advances the epoch exactly once and preserves the session and match generation. Provider ownership does not grant gameplay authority. Only explicit session creation bootstraps an authority; a provider promotion cannot create one.

`LobbyRestoreState` retains the immutable roster, allocation high-water mark, session clock, authenticated subject bindings, and disconnected reservation deadlines. Subjects are opaque authenticated adapter identities, not credentials or player-supplied wire claims. These mappings are distributed only inside the admitted session so the replacement can authenticate existing-player resume, including the former host. No EOS SDK or Godot types enter Core.

## Detection, freeze and agreement

The online composition attaches `SessionMigration` to the existing single lobby receive loop. A missing connection or two seconds without an accepted checkpoint freezes client gameplay. Transient loss continues through the existing authenticated reconnect path. Unexpected loss waits the advertised reconnect grace (30 seconds by default, with a three-second migration minimum) before one election attempt. An intentional host Leave freezes that host immediately, announces departure reliably, and drains for up to one second; survivors can begin agreement without waiting the full grace.

For a roster of two or more players, authority requires recent checkpoint acknowledgement from a strict majority, counting itself. A two-player host therefore requires its client's acknowledgement. The lease expires two seconds after the acknowledged checkpoint was published, measured by monotonic elapsed time as well as the fixed-step delta. Delayed or replayed acknowledgements cannot extend that deadline; a suspended process checks expiry before its next simulation step. A partitioned old host freezes when it loses that quorum, before the survivor's reconnect grace permits promotion. It retains the prior electorate while frozen; it cannot regain authority by shrinking away unreachable players. Authenticated resume and migration acknowledgements continue while gameplay is frozen. Reliable pause/running control propagates the lease state to reachable clients, including one-way partitions, without stopping checkpoint acknowledgements needed for recovery. Failure to recover within grace stops the session.

The minimum automatic migration roster is two total players. Election uses the connected roster in the checkpoint, excluding the old host. Exactly two players permit recovery by the sole eligible survivor after the lease and host-loss policy have fenced the old authority. Rosters of three or more still require at least two eligible survivors, the lowest stable PlayerId as candidate, and every eligible survivor's agreement on the same checkpoint digest. Callback order and EOS ownership are irrelevant. Missing candidates, disagreement, or missing required survivors fail after a 20-second agreement deadline. A larger roster cannot use the two-player exception merely because only one client remains reachable.

Peers retain four checkpoints. The sole two-player survivor selects its newest valid externally retained recoverable checkpoint; larger elections select the newest exact checkpoint present in every eligible survivor's offer. Each survivor validates the selection against its own retained bytes and current phase/match before voting. A candidate cannot substitute a newly invented checkpoint. Each process makes at most one attempt per old epoch; failures are terminal for that session attempt. Sequential migrations use a fresh electorate and the next epoch. A sole replacement excludes the disconnected former-host reservation from its acknowledgement electorate until that player actually returns; after return, the two-player lease applies again. Keeping a reservation alone cannot prevent the replacement from continuing, and losing a returned client cannot silently shrink its electorate.

Expanding a two-player roster cannot silently retire that lease. The original client must acknowledge a checkpoint containing the larger roster before the host may replace the two-player acknowledgement electorate. Accepting that larger checkpoint retires older two-player copies, and a currently larger roster cannot select a two-player checkpoint. Otherwise an isolated client could recover alone from its old roster while the original host continues with a newly joined peer. If expansion cannot be acknowledged, the old host still requires the original client and freezes on its loss.

## Complete checkpoint

The version-one `TC` checkpoint composes the existing complete `TR` resume codecs with authority continuation:

- Stable session, match generation, host, epoch, roster, reconnect generations/reservations, session clock and player allocation high-water mark.
- Simulation tick; complete vehicle aggregates, transforms, commanded and observed linear/angular velocity, handling/surface state, HP, damage attribution, collision cooldowns, accepted physical effects, life generations and respawn deadlines.
- Held slots and tokens, active missiles and IDs/lifetime, item revision and the highest token ever issued, including consumed/departed grants.
- Pickup availability, absolute cooldown deadlines, claim identities/tokens, spawn revision and the complete deterministic selector state.
- Match phase/countdown, target, score rows, consumed-life watermarks, winner/finish state and revision.
- Current immutable damage, item, respawn, match and pickup tuning; current native movable-prop poses and velocities as plain numeric records.

Production movement tuning and arena geometry remain the matching-build contracts used by the existing host session. There is no runtime configuration mutation API in the implemented game. Custom injected test item selectors deliberately cannot be captured as portable authority. Production item selection uses a specified SplitMix64 stream rather than opaque `System.Random` state; a restore continues the exact stream without replaying draws.

The envelope is bounded to 64,000 bytes, versioned, SHA-256 integrity checked, and validated through the existing nested codecs. Complete host construction is validated before a checkpoint is retained. A replacement is built separately before it becomes live authority, so malformed state cannot partially install. The digest detects corruption and identifies agreement; authenticated transport supplies sender trust, not the digest.

## Cadence, retention and rollback

The host publishes a complete checkpoint reliably every 0.5 seconds to admitted peers, in lobby and arena. Four retained copies cover approximately 1.5 seconds between oldest and newest boundaries. The actual payload varies with roster, projectiles, damage memory and lifetime score rows. The checkpoint payload ceiling is 128,000 bytes/second per recipient, or 896,000 bytes/second for seven recipients, before transport framing/retransmission. Pause/running control, acknowledgements and ordinary vehicle/item traffic are additional. EOS metadata stores only low-frequency routing/epoch coordination; it never stores gameplay checkpoints.

A healthy connection normally rolls back at most one cadence interval plus delivery delay. Selection refuses an arena checkpoint ahead of a survivor's observed authoritative tick or more than 240 ticks (four simulation seconds) behind it. The checkpoint acknowledgement lease prevents the old authority from progressing indefinitely without external recoverable copies. Network stalls that prevent a recent common boundary produce failure rather than an unbounded rollback. The four-second limit is relative to observed authoritative state; unobserved in-flight state and wall-clock outage time are not a promise of lossless recovery.

## Restore and rebind

1. Freeze gameplay and native prop progression while authority is uncertain.
2. Reconnect the EOS star to the deterministic candidate, using authenticated membership and the retained subject map.
3. Select the latest safe external checkpoint for a sole two-player survivor, or exchange digests and collect votes on the newest common boundary for a larger roster.
4. Commit the next epoch once; restore lobby authority with all Ready flags cleared.
5. Reconstruct the new host's existing Core vehicle/item/spawn/match authority. Keep stable vehicle IDs and reserve remote players for fresh authenticated bindings.
6. Rebind surviving clients through the existing reconnect-generation checks. Publish the existing complete resync checkpoint.
7. Discard pending input/use commands, seed acknowledgements, replace snapshot history, reset interpolation/correction/collision feedback, and reseed destruction presentation. Existing surviving Godot vehicle nodes are reused. Props switch to the appropriate host/replica role and restore numeric velocity as well as pose.
8. Resume from the coherent boundary. Provider ownership/metadata is coordinated afterward by the actual EOS owner; it does not decide the gameplay host. Delayed provider snapshots cannot lower the established authority epoch or replace its gameplay host, while ordinary membership and ownership fields can still refresh.

The former host may resume its reserved stable player identity as an ordinary client, with no Start/Return privilege. The local resume locator now also supports the original host identity. Restart reads current lobby host/epoch routing and still requires authenticated subject/generation validation by Core. Reservation expiry remains bounded; return after expiry does not reclaim the old slot.

## Stale traffic and one-shot safety

Lobby protocol version three carries host and epoch; all post-admission commands check the authority fence. The version-two `TG` envelope checks session, authority epoch and recipient connection generation before any nested vehicle, item, match or prop codec/event consumer runs. Native connection nonces, subscription generations, monotonic peer handles and coordinator operation generations reject retired connections and delayed callbacks. The version-two `TP` standings diagnostics also fence authority epochs and use the elected host identity. Online compatibility is `trackstorm-lobby-7`: two-player migration must not mix with older hosts that lack the two-player acknowledgement lease.

Restore publishes current item and score state with empty historical event/delta lists. Token high-water marks, missile IDs, life generations, consumed-death watermarks, spawn deadlines and revisions continue from the checkpoint. Pending uses/inputs do not cross the transition. Committed damage is not reapplied when physical continuation effects are restored. Later one-shot outcomes are evaluated by the original authorities and scoped to the new epoch. Rollback can discard outcomes after the selected boundary; it never adds those abandoned outcomes onto the restored totals.

## Failure and verification

Unrecoverable selection, agreement timeout, invalid state, or lost authority quorum stops gameplay, releases the transport, and exposes a host-migration failure with a usable Leave/menu path. There is no fallback to independent surviving simulations, dedicated server, cloud checkpoint store or manual transfer UI.

Core tests cover exact election agreement, single commit, stale epochs, lobby identity/Ready restore, former-host privileges, compound codec corruption, tokens, selector continuation, pickup revisions, death attribution, respawn and winner/score idempotency. Client tests use production EOS framing with authenticated fake datagrams for lobby/match migration, transient recovery and bounded disagreement. These tests do not establish real EOS connectivity.

`check-migration.ps1 -GodotPath <exe> -Players 2` uses real local UDP peers and isolated native worlds: intentional lobby host leave, former-host rebind, normal Ready/Start, active-match authority loss, sequential epochs, retained native vehicles, reset prediction and complete gameplay state. `check-migration-processes.ps1 -GodotPath <exe> -Players 2` launches independent Godot processes and forcibly terminates the host after checkpoints exist; the survivor must resume the same epoch and roster. Both scripts also support `-Players 3` (the default) to retain multi-survivor and process-kill regression coverage. Both use an explicitly trusted local identity seam, not EOS authentication. Reconnect, lobby, vehicle, combat, spawning, lifecycle and match harnesses remain applicable regressions.

Real EOS multi-PC host loss, service ownership notification timing, Locked migration admission, changed-network former-host restart and long-session Internet behavior require separate-device verification. Never infer these from local UDP or fake-provider success. See [EOS development](../eos-development.md).

[Feature index](README.md)
