# Host migration and match rehosting

## Authority and identity

Trackstorm owns the logical session, stable player IDs, `CurrentHostId`, `AuthorityEpoch`, and match generation. The initial host has player ID 1; later hosts keep their existing player IDs. A successful transfer advances the epoch exactly once and preserves the session and match generation. Provider ownership does not grant gameplay authority. Only explicit session creation bootstraps an authority; a provider promotion cannot create one.

`LobbyRestoreState` retains the immutable roster, allocation high-water mark, session clock, authenticated subject bindings, and disconnected reservation deadlines. Subjects are opaque authenticated adapter identities, not credentials or player-supplied wire claims. These mappings are distributed only inside the admitted session so the replacement can authenticate existing-player resume, including the former host. No EOS SDK or Godot types enter Core.

## Detection, freeze and agreement

The online composition attaches `SessionMigration` to the existing single lobby receive loop. A missing connection or two seconds without an accepted checkpoint freezes client gameplay. Transient loss continues through the existing authenticated reconnect path. Unexpected loss waits the advertised reconnect grace (30 seconds by default, with a three-second migration minimum) before one election attempt. An intentional host Leave freezes that host immediately, announces departure reliably, and drains for up to one second; survivors can begin agreement without waiting the full grace.

For a migration-capable roster, authority requires recent checkpoint acknowledgement from a strict majority, counting itself. An acknowledgement must refer to a retained checkpoint no more than 120 session ticks old and must have arrived within two seconds. A partitioned old host freezes when it loses that quorum. It retains the prior electorate while frozen; it cannot regain authority by shrinking away unreachable players. Authenticated resume and migration acknowledgements continue while gameplay is frozen. Failure to recover within grace stops the session.

Election uses the connected roster in the checkpoint, excluding the old host. At least two eligible survivors are required. The lowest stable PlayerId is the candidate. Every eligible survivor must agree on the same checkpoint digest and candidate; callback order and EOS ownership are irrelevant. Missing candidates, disagreement, or missing survivors fail after a 20-second agreement deadline. This conservative policy favors coherent authority over continuing through multiple simultaneous failures. Two-player sessions retain reconnect behavior but cannot automatically migrate to a single survivor.

Peers retain four checkpoints. The candidate selects the newest exact checkpoint present in every eligible survivor's offer. Each survivor validates the selection against its own retained bytes and current phase/match before voting. A candidate cannot substitute a newly invented checkpoint. Each process makes at most one attempt per old epoch; failures are terminal for that session attempt. Sequential migrations use a fresh electorate and the next epoch.

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

The host publishes a complete checkpoint reliably every 0.5 seconds to admitted peers, in lobby and arena. Four retained copies cover approximately 1.5 seconds between oldest and newest boundaries. The actual payload varies with roster, projectiles, damage memory and lifetime score rows. The hard payload budget is 128,000 bytes/second per recipient, or 896,000 bytes/second for seven recipients, before transport framing/retransmission. Ordinary vehicle/item traffic is additional. EOS metadata stores only low-frequency routing/epoch coordination; it never stores gameplay checkpoints.

A healthy connection normally rolls back at most one cadence interval plus delivery delay. Selection refuses an arena checkpoint ahead of a survivor's observed authoritative tick or more than 240 ticks (four simulation seconds) behind it. The checkpoint acknowledgement lease prevents the old authority from progressing indefinitely without external recoverable copies. Network stalls that prevent a recent common boundary produce failure rather than an unbounded rollback. The four-second limit is relative to observed authoritative state; unobserved in-flight state and wall-clock outage time are not a promise of lossless recovery.

## Restore and rebind

1. Freeze gameplay and native prop progression while authority is uncertain.
2. Reconnect the EOS star to the deterministic candidate, using authenticated membership and the retained subject map.
3. Exchange retained checkpoint digests, select their newest common boundary, and collect survivor votes.
4. Commit the next epoch once; restore lobby authority with all Ready flags cleared.
5. Reconstruct the new host's existing Core vehicle/item/spawn/match authority. Keep stable vehicle IDs and reserve remote players for fresh authenticated bindings.
6. Rebind surviving clients through the existing reconnect-generation checks. Publish the existing complete resync checkpoint.
7. Discard pending input/use commands, seed acknowledgements, replace snapshot history, reset interpolation/correction/collision feedback, and reseed destruction presentation. Existing surviving Godot vehicle nodes are reused. Props switch to the appropriate host/replica role and restore numeric velocity as well as pose.
8. Resume from the coherent boundary. Provider ownership/metadata is coordinated afterward by the actual EOS owner; it does not decide the gameplay host.

The former host may resume its reserved stable player identity as an ordinary client, with no Start/Return privilege. The local resume locator now also supports the original host identity. Restart reads current lobby host/epoch routing and still requires authenticated subject/generation validation by Core. Reservation expiry remains bounded; return after expiry does not reclaim the old slot.

## Stale traffic and one-shot safety

Lobby protocol version three carries host and epoch; all post-admission commands check the authority fence. The version-two `TG` envelope checks session, authority epoch and recipient connection generation before any nested vehicle, item, match or prop codec/event consumer runs. Native connection nonces, subscription generations, monotonic peer handles and coordinator operation generations reject retired connections and delayed callbacks. Online compatibility is `trackstorm-lobby-6`.

Restore publishes current item and score state with empty historical event/delta lists. Token high-water marks, missile IDs, life generations, consumed-death watermarks, spawn deadlines and revisions continue from the checkpoint. Pending uses/inputs do not cross the transition. Committed damage is not reapplied when physical continuation effects are restored. Later one-shot outcomes are evaluated by the original authorities and scoped to the new epoch. Rollback can discard outcomes after the selected boundary; it never adds those abandoned outcomes onto the restored totals.

## Failure and verification

Unrecoverable selection, agreement timeout, invalid state, or lost authority quorum stops gameplay, releases the transport, and exposes a host-migration failure with a usable Leave/menu path. There is no fallback to independent surviving simulations, dedicated server, cloud checkpoint store or manual transfer UI.

Core tests cover exact election agreement, single commit, stale epochs, lobby identity/Ready restore, former-host privileges, compound codec corruption, tokens, selector continuation, pickup revisions, death attribution, respawn and winner/score idempotency. Client tests use production EOS framing with authenticated fake datagrams for lobby/match migration, transient recovery and bounded disagreement. These tests do not establish real EOS connectivity.

`check-migration.ps1 -GodotPath <exe>` uses three real local UDP peers and isolated native worlds: intentional lobby host leave, former-host rebind, normal Ready/Start, active-match authority loss, sequential epochs, retained native vehicles, reset prediction and complete gameplay state. `check-migration-processes.ps1 -GodotPath <exe>` launches three independent Godot processes and forcibly terminates the test host after checkpoints exist; both survivors must resume the same epoch and roster. Both use an explicitly trusted local identity seam, not EOS authentication. Reconnect, lobby, vehicle, combat, spawning, lifecycle and match harnesses remain applicable regressions.

Real EOS multi-PC host loss, service ownership notification timing, Locked migration admission, changed-network former-host restart and long-session Internet behavior require separate-device verification. Never infer these from local UDP or fake-provider success. See [EOS development](../eos-development.md).

[Feature index](README.md)
