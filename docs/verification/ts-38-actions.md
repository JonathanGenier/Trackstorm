# TS-38 / TS-39 Developer Options action correction — 2026-09-16

This correction implements the approved defaults/apply clarification in TS-38 comment 10047 and TS-39 comment 10048, also reflected in PR #31 and the user's direct instruction. It remains on `ts-38-pg` and updates the existing draft PR. Migration requirements and final integrated Story verification/critique remain pending; this is not a completed-Story review.

## Resulting behavior

- **Apply Settings** is the single gameplay tuning commit action. Numeric Enter alone does not apply. Parsing feeds the existing Core authority/validation path; rejected values remain editable and show failure. Accepted tuning applies live, synchronizes normally and automatically saves. A failed save retains accepted live tuning and the prior file, explicitly instructing the user to press Apply Settings again. Unchanged Apply retries saving without a new configuration revision.
- **Discard Changes** copies the currently active authoritative configuration into the editors, replacing unapplied input. It does not mutate gameplay, change revision or write storage.
- **Reset to Defaults** stages the complete production hosted-game preset only. It does not mutate gameplay, revision, the in-memory persistence owner or the file. The staged preset survives automatic refresh. Apply then requests the whole preset through normal validation, including fields whose authoritative values changed after the editor opened, and saves the accepted defaults.
- `GameplayConfiguration.HostedDefaults` is the single production hosted preset. It composes the existing configuration-owner defaults with **MaxHP = 1000**. Host arena startup, the session fallback, host-local loading/missing keys and Reset use it. Plain Core fixture defaults still use 100 HP.

Normal authority restrictions and runtime validation remain in effect for Reset + Apply, including match-state constraints. Unknown persisted keys retain the existing schema-tolerant preservation behavior; they do not affect gameplay. No separate normal Save button remains.

## Files changed

| Files | Purpose |
| --- | --- |
| `code/Core/Development/GameplayConfiguration.cs` | Defines the canonical hosted defaults. |
| `code/Client/Development/DeveloperOptionsDraft.cs` | Isolates pending editor text, Discard, Reset and request parsing without gameplay/storage authority; preserves integer precision. |
| `code/Client/Development/DeveloperOptionsPanel.cs` | Wires Apply Settings / Discard Changes / Reset to Defaults; removes numeric Enter commits and separate Save; places actions before explanatory text. |
| `code/Client/Development/DeveloperSettingsStore.cs` | Uses hosted defaults and reports Apply-based persistence retry. |
| `code/Client/Networking/DevelopmentSession.cs`, `code/Client/Networking/NetworkVehicleArena.cs` | Use the same production preset in session fallback and actual hosted arena composition. |
| `code/TransportTests/DeveloperOptionsTests.cs` | Adds nine deterministic cases for defaults, draft isolation, validation, full reset and persistence failure/retry. |
| `code/Client/Verification/DeveloperOptionsIntegrationChecks.cs` | Exercises actual buttons, active-state/file isolation, defaults, explicit commit, rejected values, real save failure/retry and host/client synchronization. |
| `code/Client/Verification/MenuIntegrationChecks.cs` | Expects the new Apply Settings action in the existing category. |
| `docs/features/developer-options.md`, `docs/features/settings.md`, `docs/features/game-menu.md` | Synchronize feature behavior and ownership documentation. |
| `docs/verification/ts-38-actions.md`, `docs/verification/README.md` | Record this correction and route to its evidence. |

## Verification

- `./check.ps1`: formatting, Debug/Release builds with zero warnings/errors, **310 Core + 150 non-native Client/transport tests per configuration**. Nine new deterministic cases cover production defaults/missing keys, Discard with current authority and maximum signed seed precision, Reset isolation and subsequent application/persistence, resetting after an authority change, four invalid-value cases, and unchanged-value save retry.
- `./check-developer-options.ps1 -GodotPath <Godot .NET executable> -Visual`: **233 write-phase assertions + 7 after a full process restart**. Uses real local UDP, production bootstrap/menu, all 59 controls and actual network impairments. Save failure is injected deterministically by occupying the isolated temporary-file path with a directory; retry goes through the actual Apply Settings button after removing that obstruction.
- `./check-menu.ps1 -GodotPath <Godot .NET executable>`: **60 assertions**, including solo Waiting, navigation, local settings, Developer Options, leave/re-entry and production Quit.
- Source/diff review: no new dependency or third-party asset, no Core dependency on Client/Godot, no parallel gameplay configuration or migration path. Affected feature links and `git diff --check` checked.

Godot runtime verification uses `C:/Users/orsin/OneDrive/Desktop/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe` as the `-GodotPath` argument above. Final rendered actions and numeric fields were visually inspected; all three tuning actions appear together at the initial 1280×720 view. Runtime artifacts are in `.godot/developer-options-checks/59beec879566465db0b311333b6ff347` and `.godot/menu-checks/2332dfd6c94e4c7b8521809ffbf23f14`. Summary logs are `.godot/ts38-actions-check.log`, `.godot/ts38-actions-runtime-final.log` and `.godot/ts38-actions-menu.log`; these are intentionally excluded from Git.

The unrelated pre-existing tab-only edit in `code/Core/Input/InputButtons.cs` triggers repository formatting/analyzer failures. Verification temporarily used equivalent normalized whitespace; the user's original bytes were restored afterward (hash verified) and excluded from this correction's commit.

## Pending scope and limitations

**TS-46 integration was not implemented.** Replacement-host authority/actions, AuthorityEpoch and stale-host fencing, tuning/revision and item RNG migration, successor-host local override isolation, migration diagnostics and successive migration tests all remain required. TS-38/TS-39 remain incomplete, and PR #31 stays draft and unmerged. Final integrated verification and critique remain deferred until migration integration.

Evidence covers deterministic tests and local rendered UDP/process-restart runs. It does not establish authenticated multi-PC EOS migration/reconnect, physical-controller ergonomics, subjective game balance/audio quality or Internet soak behavior. Earlier broader gameplay/network evidence remains in the [independent checkpoint](ts-38.md); those suites were not all rerun for this focused editor/default correction.
