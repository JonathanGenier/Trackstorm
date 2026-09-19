# Game Version and Multiplayer Compatibility

## Canonical Build Identity

The root `Directory.Build.props` defines `TrackstormVersion` in canonical `0.0.1.N` form. Revision zero establishes the sequence; each subsequent Story merge advances the fourth component exactly once. Revisions range from 0 through 65534 to fit .NET assembly metadata. Leading zeroes, whitespace, suffixes, missing components and other release prefixes are invalid.

MSBuild derives assembly, file, informational and `TrackstormVersion` assembly metadata from that property. Core's immutable `GameVersion.Current` reads generated metadata once. Runtime systems never read project XML or depend on the checkout. The multiplayer menu, Developer Options diagnostics, EOS metadata and session messages use that provider. Explicit version injection on session construction supports deterministic mixed-build tests; production composition uses the canonical provider.

## Story Sequencing

The required GitHub Actions `verify` job checks every PR targeting `main`. It checks out the actual PR head, fetches current `origin/main`, reads both canonical properties and requires exactly `base revision + 1`, with the release prefix unchanged. It also requires current main to be an ancestor of the Story head. A parallel Story that was previously valid must synchronize and increment again after another Story merges. Diagnostics show expected and actual values; missing or duplicate properties fail closed. Only a base without a version property permits initialization at `0.0.1.0`.

Follow the [canonical Story version procedure](../workflow.md#canonical-story-version-procedure) before implementation and finalization: fetch and synchronize with current main, read its `Directory.Build.props`, then set only `TrackstormVersion` to the next fourth-component revision. Main `0.0.1.4` requires Story `0.0.1.5`. Do not increment from the previous local value, branch creation time, commit count or another Story branch, and do not increment on every Codex/agent run when the expected value is already present. If another Story advances main, synchronize and derive the expected value again. Only the initial TS-66 establishment against main without a canonical property starts at `0.0.1.0`.

Run `./tools/check-version.ps1` after synchronization/version adjustment and before final delivery. The script reads the current-main Git ref and the checked-out Story property; callers must fetch first. CI does this in the existing `verify` job, which remains the single enforcement path and preserves normal restore, formatting, build and test steps. Existing up-to-date-main branch protection and the required `verify` check remain necessary to close the race between CI completion and merging another Story. A future release-prefix change or exhausted revision range requires an explicit sequencing-policy change.

`./tools/test-version.ps1` runs deterministic sequencing/parser checks, also invoked by `check.ps1` and CI. `GameVersionTests` separately checks runtime parsing and generated metadata against the repository source.

## Discovery and Authoritative Admission

[EOS lobbies](eos-lobbies.md) publish `version` alongside existing minimal coordination attributes. The protocol bucket remains a schema marker, distinct from the exact game build. Structurally valid mismatched lobbies stay visible and searchable, with Join disabled and a message containing hosted and local versions. Missing or invalid metadata is incompatible and shown as `unknown/invalid`; untrusted text is never echoed. Public and Locked lobbies use the same gate before credential validation. Refreshed join metadata is checked again to handle discovery races.

[Session admission](sessions.md) transmits the runtime version with Join and Resume intents. Core `LobbyAuthority.Join`, `Add` and `Resume` require an explicit version and validate exact equality before allocating an ID, publishing a roster mutation or rebinding a retained player. This rule has no EOS dependency and also governs Direct-IP. Browser metadata or correct access credentials cannot grant gameplay authority.

The host driver sends a distinct bounded reliable version rejection containing its validated version. It allows one second for delivery before disconnecting, ignores further admission on that rejected connection, and sends no roster or gameplay bootstrap to it. The client renders the mismatch using its own canonical version and preserves that reason across disconnect/timeout handling. No player slot, authenticated reservation, player-ID increment or gameplay state is created by the rejected attempt. Transport/EOS membership established before admission remains coordination state and is cleaned up through the existing session lifecycle.

## Resume and Boundaries

[Reconnect](reconnection.md) requires exact equality in addition to existing subject, session, generation, authority-epoch, match-retention and peer checks. An incompatible return cannot reactivate a player or trigger a gameplay checkpoint; its disconnected reservation is unchanged and remains available until the match ends. A later compatible, authorized return can reclaim that same player.

New-player arena admission uses the existing join-in-progress flow. The version gate precedes its phase/capacity checks and checkpoint activation, without a separate compatibility policy. Match retention and host migration follow their existing policies; version compatibility does not grant authority or extend reservations. Protocol negotiation, patch delivery and updaters remain outside this system.

## Runtime Verification

Run `check.ps1`, `check-online-lobby.ps1`, `check-lobby.ps1` and `check-reconnect.ps1` for deterministic and local native coverage. The online UI harness includes a visible mismatch with disabled Join; networking tests bypass discovery, cover older/newer clients and verify no roster, identity or bootstrap leak.

Separate-device acceptance requires two exported builds with different canonical revisions: host Public and Locked lobbies, discover from the other build, confirm both version values and blocked Join, then retry with matching builds and confirm ordinary join, Ready/Start and reconnect. Use independent PCs/profiles as described in [EOS setup](../eos-development.md). Local fake-provider and UDP tests do not establish this exported-build or two-PC result.
