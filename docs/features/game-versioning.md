# Game Version and Multiplayer Compatibility

## Canonical Build Identity

The root `Directory.Build.props` is the one canonical Trackstorm version source: exactly `MAJOR.RELEASE.PR`. For the current generation `MAJOR` is `0`; `RELEASE` and `PR` are integers from 0 through 65534. Release zero is valid. Leading zeroes, whitespace, suffixes, missing components and obsolete four-component canonical values are rejected.

The Release 0.1.0 baseline is **`0.1.0`**, with Windows file/product metadata **`0.1.0.0`**. Runtime UI, generated assembly metadata and multiplayer discovery/admission use the same canonical identity. Normal progression from this release is `0.1.1`, `0.1.2`, and so on. The approved hosted gameplay preset is described in [Developer Options](developer-options.md#host-local-persistence).

Core's immutable `GameVersion.Current` reads generated assembly metadata once; runtime code never reads project XML. UI, diagnostics, EOS product/discovery metadata and Join/Resume intents use that exact three-component identity. MSBuild derives informational and `TrackstormVersion` assembly metadata unchanged. .NET assembly/file versions use the deterministic four-slot container mapping `MAJOR.RELEASE.PR.0`; this is not a canonical Trackstorm value and is never accepted by multiplayer parsing. Client assembly metadata is generated as well as Core metadata.

The Windows `application/file_version` and `application/product_version` fields in tracked `export_presets.cfg` are synchronized representations of the canonical source, not independent inputs. `sync-story-version.ps1` updates them to `MAJOR.RELEASE.PR.0` whenever it establishes or recalculates a Story version, while preserving unrelated export preferences. Fixed repository identity is `application/company_name="Thantrick"` and `application/product_name="Trackstorm"`; every checkout inherits these values, and synchronization preserves them unchanged. `check-version.ps1` and CI reject missing, blank, duplicated, malformed, incorrectly cased or drifted metadata. Synchronization fails closed on invalid identity rather than treating it as a local preference.

## Story Sequencing

Follow the [canonical Story version procedure](../workflow.md#canonical-story-version-procedure) before implementation and finalization. Fetch and synchronize with current main, then run `sync-story-version.ps1`; it sets only the third component to `main.PR + 1` without changing `MAJOR.RELEASE` and synchronizes both tracked preset fields in the same operation. Main `0.1.4` requires canonical `0.1.5` and Windows preset `0.1.5.0`. Never derive it from branch age, commit count, a previous local value or another Story branch, and never increment blindly on every agent run. Repeated runs against unchanged main produce no additional file changes. If main advances, synchronize and recalculate again.

The existing GitHub Actions `verify` job is the single enforcement path. It checks out the actual PR head, fetches current `origin/main`, and invokes `check-version.ps1` for PRs targeting main. The script rejects non-ancestor/stale main, compares the canonical source against the expected transition, and rejects preset drift or structurally invalid required preset fields. Missing, duplicate, malformed, wrong-major, unchanged and skipped canonical revisions also fail. Normal restore, formatting, builds and tests remain in the same job. Required `verify` status and up-to-date-main protection must remain enabled to close the race between validation and merge. After a successful push to `main`, CI also re-runs the latest completed Pull Request CI run for each still-open PR targeting `main`. Because that rerun executes the same `verify` job and fetches current `main`, a previously green PR whose branch or Story version became stale turns failing without waiting for a developer to push another commit. PRs with an already-running CI run are skipped to avoid duplicate work.

### Explicit release authorization

A dedicated Jira release-version Story updates `.github/version-transition.json`, whose exact fields are `kind`, `story`, `from` and `to`. For example:

```json
{"kind":"release","story":"TS-123","from":"0.1.15","to":"0.2.0"}
```

CI requires a newly changed declaration relative to main, a Jira key matching the PR's branch (for example `ts-123-jg`), an exact source/target match, the next release (`main.RELEASE + 1`) and revision zero. A prefix change is never authorized just because it ends in `.0`. A stale declaration, reused declaration, arbitrary jump to `0.8.0`, mismatched Story or nonzero reset fails. Reviewers verify that the named Jira Story is actually the approved release work; CI does not authenticate Jira or replace code review. After merge, leave the declaration unchanged for normal Stories, which continue `0.2.1`, `0.2.2`, etc.

The declaration is an auditable transition authorization, not a second canonical build source: builds and runtime never consume it. The only legacy source exception is TS-70's exact migration declaration from `0.0.1.0` to approved `0.0.15`; missing sources and other four-component values are not initialization shortcuts. Changing release policy or exhausting the numeric range requires explicit reviewed work.

TS-94 alone authorizes a one-time Release 0.1.0 baseline correction using `kind: baseline-correction`, `story: TS-94`, and exact `from`/`to` values of `0.1.0`. Run synchronization with `-Kind baseline-correction` for this correction. The declaration must be new relative to main and match the branch Jira key; it does not allow other issues, other versions or reuse after merge to skip an increment. Subsequent normal Stories preserve the declaration and advance `0.1.0 -> 0.1.1 -> 0.1.2`; future release Stories replace it with their release authorization.

Run `./tools/check-version.ps1` after synchronization/version adjustment and before delivery. It does not fetch itself; CI fetches explicitly, and local callers must do likewise. `./tools/test-version.ps1` runs deterministic parser, transition, preset synchronization, idempotence and drift-rejection cases, also included by `check.ps1` and CI; `GameVersionTests` covers runtime parsing, exact compatibility and generated metadata.

## Godot and exported identity

`project.godot` has no independently maintained `config/version`. Tracked `export_presets.cfg` physically contains synchronized Windows file/product values using the derived `.0` container mapping; for canonical `0.1.0`, both are `0.1.0.0`. Story version tooling updates those two fields without changing other export preferences. The enabled `addons/trackstorm_version` editor/export plugin independently reads and validates the canonical MSBuild property at editor/export time, sets Godot's in-memory `application/config/version` before packing, and overrides Windows numeric resources as an additional export-time guarantee. Tracked synchronization and export-time verification are complementary. The packaged Godot version and the in-game title remain the exact three-component value.

The ordinary Client bootstrap sets runtime Godot metadata from `GameVersion.Current` for editor-launched games. `-- --version-check` prints the runtime and embedded Godot values and fails an exported executable whose embedded version disagrees. Exports need the enabled plugin, the canonical source and the normal .NET build. Windows is the configured export platform; other platforms retain the canonical Godot setting but platform-specific resource metadata requires validation when an export target is introduced.

The plugin uses Godot's [EditorExportPlugin option overrides](https://docs.godotengine.org/en/stable/classes/class_editorexportplugin.html#class-editorexportplugin-private-method-get-export-options-overrides), shared by editor and command-line exports. Run `check-build-version.ps1` for native runtime/export identity verification after a build/export.

## Discovery and Authoritative Admission

[EOS lobbies](eos-lobbies.md) publish `version` alongside existing minimal coordination attributes. The protocol bucket remains a schema marker, distinct from the exact game build. Structurally valid mismatched lobbies stay visible and searchable, with Join disabled and a message containing hosted and local versions. Missing or invalid metadata is incompatible and shown as `unknown/invalid`; untrusted text is never echoed. Public and Locked lobbies use the same gate before credential validation. Refreshed join metadata is checked again to handle discovery races.

[Session admission](sessions.md) transmits the runtime version with Join and Resume intents. Core `LobbyAuthority.Join`, `Add` and `Resume` require an explicit version and validate exact equality before allocating an ID, publishing a roster mutation or rebinding a retained player. This rule has no EOS dependency and also governs Direct-IP. Browser metadata or correct access credentials cannot grant gameplay authority.

The host driver sends a distinct bounded reliable version rejection containing its validated version. It allows one second for delivery before disconnecting, ignores further admission on that rejected connection, and sends no roster or gameplay bootstrap to it. The client renders the mismatch using its own canonical version and preserves that reason across disconnect/timeout handling. No player slot, authenticated reservation, player-ID increment or gameplay state is created by the rejected attempt. Transport/EOS membership established before admission remains coordination state and is cleaned up through the existing session lifecycle.

## Resume and Boundaries

Retained-match inspection and abandonment carry the runtime version over the same authenticated lobby stream. The host rejects incompatible control requests before querying or releasing a reservation. Version rejection and reservation responses have distinct packet kinds; the menu preserves both the canonical local/host mismatch message and the local routing hint without releasing the host reservation. A later compatible build under the same account can retry inspection and reclaim that unchanged reservation.

[Reconnect](reconnection.md) requires exact equality in addition to existing subject, session, generation, authority-epoch, match-retention and peer checks. An incompatible return cannot reactivate a player or trigger a gameplay checkpoint; its disconnected reservation is unchanged and remains available until the match ends. A later compatible, authorized return can reclaim that same player.

New-player arena admission uses the existing join-in-progress flow. The version gate precedes its phase/capacity checks and checkpoint activation, without a separate compatibility policy. Match retention and host migration follow their existing policies; version compatibility does not grant authority or extend reservations. Protocol negotiation, patch delivery and updaters remain outside this system.

## Runtime Verification

Run `check.ps1`, `check-online-lobby.ps1`, `check-lobby.ps1` and `check-reconnect.ps1` for deterministic and local native coverage. The online UI harness includes a visible mismatch with disabled Join; networking tests bypass discovery, cover older/newer clients and verify no roster, identity or bootstrap leak.

Separate-device acceptance requires two exported builds with different canonical revisions: host Public and Locked lobbies, discover from the other build, confirm both version values and blocked Join, then retry with matching builds and confirm ordinary join, Ready/Start and reconnect. Use independent PCs/profiles as described in [EOS setup](../eos-development.md). Local fake-provider and UDP tests do not establish this exported-build or two-PC result.
