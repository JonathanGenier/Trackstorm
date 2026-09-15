# TS-44: unreliable ordering correction

Date: 2026-09-14. Existing Story branch: `ts-44-jg`; existing PR: #15. Reviewed and confirmed remote head before editing: `a6d02377b51753bb1ff6948694fff28f76e49a59`. `git fetch origin` confirmed `origin/main` remained `d6f39a4`, already an ancestor of this branch. No branch, PR, release or Jira issue was created for this correction.

## Scope and Jira access

The user explicitly authorized one correction of the reviewed EOS unreliable-message ordering defect and its verification. Jira refresh failed with: `Access denied. You don't have permission to search content. Please contact your administrator to request access.` Details: code `403`, message `The app is not installed on this instance`. Work therefore proceeds only against the explicit defect request. This report does not invent Jira criteria or declare TS-44/TS-48 acceptance complete.

## Reproduction and root cause — VERIFIED

`ReversedProductionVehicleAndPropMessagesBothAdvance` uses the production EOS gateway, real framing/handshake, the fake native seam and production `VehicleNetworkDriver` with `ObserveProps` configured. The wire reverses unreliable datagrams while preserving reliable order. Over 30 fixed ticks, ten vehicle/prop publication pairs are sent.

On the reviewed gateway, props reached tick 30 but the client received only one snapshot: the initial reliable item publication. All ten periodic vehicle snapshots were suppressed. The final regression assertion expects eleven total snapshots, latest vehicle/prop tick 30 and advancing input acknowledgements. Running that exact assertion against the reviewed gateway failed **expected 11, actual 1**. Baseline source was restored temporarily for the confirming run and corrected source restored in `finally`; no baseline code remains in the change.

The previous unreliable stream had one receive assembly and one latest sequence shared by all application purposes. A later prop message advanced that sequence, causing the preceding vehicle message to be classified as stale. Reversing fragments within one message could pass while independent message reordering failed. This defect belongs to EOS receive assembly; it is not an explanation for historical GNS correction spikes.

## Fix and bounds

- Add Client-only `EosUnreliableWindow`, keyed by the existing transport sequence, with sixteen fixed assembly slots. It accepts the newest sequence and its fifteen predecessors, using signed half-range comparison for advancement and unsigned distance for window membership, including wrap.
- Complete independent messages in completion order, once each. The gateway never reads vehicle, prop or other application formats. Consumer-level freshness checks remain unchanged.
- Newer sequences evict assemblies outside the sixteen-message window. Packets at least sixteen sequences behind and ambiguous half-range serials are dropped. Within the window, incomplete messages expire on Poll one second after their first accepted fragment; duplicates and further fragments do not extend the deadline. Completed and expired sequences retain tombstones until eviction, preventing duplicate delivery or resurrection.
- Reliable ordered assembly remains separate and unchanged. Add only a reset operation to the reusable fragment accumulator; no new buffer allocation is needed when an unreliable slot is reused.
- Sixteen unreliable 64 KiB buffers plus one reliable buffer cost **1,114,112 bytes per peer**, or **7,798,784 bytes for seven peers**, excluding object metadata, native queues and caller-owned completed messages. This deliberately spends more bounded memory than the former two-buffer design to avoid per-fragment/per-message assembly allocation churn.
- Expiry scans at most sixteen entries per peer per Poll; each newer received sequence scans at most sixteen entries for eviction. Existing limits remain: 256 packets and 256 callbacks per Poll, 256 queued completed messages, 64 KiB payload, 1,170-byte native datagram, at most 58 fragments and seven remote peers. Completed payloads retain their separate stable owned copy.
- The gateway still validates both connection nonces before accessing the window. Disconnect removes peer state; Stop/Dispose remove all peer windows. Idle Poll expires incomplete work even without later traffic.

Sixteen slots accommodate eight pairs of vehicle/prop publications (roughly 400 ms at 20 pairs/s). This is a sequence-distance bound, not a promised network latency tolerance: higher publication rates shorten the time represented by the window. Delayed unreliable traffic beyond the window or expiry is intentionally lost; it cannot block later traffic.

### Compatibility — INFERRED from unchanged framing

The fix changes receiver policy only. Packet header, channel mapping, sequence representation, nonces and application payloads are unchanged, so `trackstorm-lobby-2` remains appropriate. No protocol bucket bump is made. Old/new peers remain wire-compatible, but an older receiver retains its old discard behavior; both receivers must run corrected code to benefit in both directions. Mixed-version native interoperability was not physically retested.

## Changed components and regression coverage

| File | Change |
| --- | --- |
| `code/Client/Networking/EosUnreliableWindow.cs` | Fixed receive/deduplication window, eviction and expiry. |
| `code/Client/Networking/EosPacketAssembly.cs` | Reusable accumulator reset; updated ownership summary. |
| `code/Client/Networking/EosP2pTransport.cs` | Use the window for unreliable receives, pass the monotonic clock and expire work during Poll. |
| `code/TransportTests/EosP2pTransportTests.cs` | Production mixed-message regression and deterministic fault/lifecycle coverage. |
| `docs/features.md` | Durable delivery semantics, bounds, rationale and compatibility. |
| This report | New verification evidence, separate from historical release/testing results. |

New or expanded EOS tests cover:

1. Reversed production vehicle-plus-prop messages with `ObserveProps`, complete publication counts and input acknowledgement advancement.
2. Two interleaved maximum-size unreliable messages, reversed fragments and duplicate fragments, completion once per message, reliable controls in order despite a third incomplete unreliable message, and stable payload ownership after every slot is reused.
3. The fifteen-behind accepted boundary and sixteen-behind rejected boundary, whole-message duplicates, wrap through zero, and ambiguous half-range serial rejection.
4. Sixty-four successive incomplete messages, eviction of earlier incomplete work and continued reliable/unreliable progress.
5. Exact one-second idle expiry, a duplicate halfway through the deadline, rejection of late restart, and subsequent fresh delivery.
6. Unchanged Poll/completed-queue bounds on both reliable and unreliable streams.
7. Two/eight-player production-driver scenarios for 600 ticks with every vehicle/prop pair reversed. Vehicle and prop ticks continue advancing; after startup each client has at most four pending inputs and acknowledgements remain within five ticks. Original non-reordered scenarios remain.
8. Four reconnect cycles per delivery mode with a held incomplete fragment, explicit disconnect/Stop, old nonce replay rejected and fresh messages accepted. Existing membership, timeout, malformed-packet and idempotent disposal tests remain.

## Commands and results

Run from the repository root. Runtime commands use `GodotPath` pointing to the installed Godot 4.7.2 Mono console executable. Dotnet runs used `DOTNET_PROCESSOR_COUNT=4`. Logs below are local ignored evidence under `.godot`, not uploaded CI artifacts.

| Command/check | Result | Local evidence |
| --- | --- | --- |
| `dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj -c Debug --filter FullyQualifiedName~ReversedProductionVehicleAndPropMessagesBothAdvance -warnaserror` with reviewed gateway | **VERIFIED FAIL**, intended red regression: expected 11 snapshots, actual 1. | `ts44-r3-baseline-confirmed.log` |
| `dotnet test code/TransportTests/Trackstorm.Transport.Tests.csproj -c Debug --filter FullyQualifiedName~EosP2pTransportTests -warnaserror` during development | All 19 then-present EOS cases passed, but two spacing analyzer errors were reported; corrected before the full clean check. This run is not counted as a clean build. | `ts44-r3-eos.log` |
| `./check.ps1` | **VERIFIED PASS**: restore/format, Debug and Release builds with zero warnings/errors; 194 Core and 91 non-native transport cases passed in each configuration, including all 20 final EOS cases. | `ts44-r3-check.log` |
| `./check-transport.ps1 -GodotPath <exe>` | **VERIFIED PASS**: 102/102 tests, including native GNS regressions and all final EOS cases; three Godot node/poll/message/cleanup cycles. | `ts44-r3-native.log` |
| `./check-network-vehicles.ps1 -GodotPath <exe> -NoBuild` | **VERIFIED PASS**: actual two-process Godot vehicle runtime; client 318 snapshots, 650 immediate frames, p99 correction 0 m, maximum 2.100008 m, last acknowledgement 955, prop replica error 0. This is GNS regression evidence. | `ts44-r3-vehicles.log`; artifacts `network-vehicle-checks/0f4828bf7dcf496caa8cfc3c1dba712a` |
| `./check-lobby.ps1 -GodotPath <exe> -NoBuild` | **VERIFIED PASS**: eight production UIs, roster/names/IDs, unready Start rejection, Ready/Start/arena/Return, rejoin, repeated arena, gameplay departure and host-loss cleanup. | `ts44-r3-lobby.log`; artifacts `lobby-checks/bdd223925b26402897478f3cbaf53f2e` |
| `./check-eos.ps1 -GodotPath <exe> -P2p` | **VERIFIED PASS**: three processes, each with three real authenticated EOS platform/login/logout and lobby/P2P notification/listen/stop plus solo Ready/Start/arena/Return cycles; terminal shutdown verified. No remote peer traffic. | `ts44-r3-eos-runtime.log` |
| `git diff --check` and complete correction inspection | **VERIFIED PASS**: reviewed all six changed/new files; Client-only receive change, unchanged Core/gameplay/framing, bounded loops and buffers, correct wrap/slot mapping, nonce checks before reassembly, reset/tombstone behavior and stable payload ownership. No threshold weakened, generated file staged or unrelated behavior changed. | Local diff against `a6d0237` |

All checks above completed successfully after the source correction; none relies on the old exported binary. No additional eight-process GNS arena impairment rerun was needed for this receive-only EOS change. The full native suite did exercise the eight-driver GNS 30 ms latency / 10 ms jitter / 2% loss scenario: p99 0.0000 m, maximum 0.4768 m, 222 snapshots/client. This is a regression check, not evidence about EOS impairment or the cause of earlier GNS spikes.

### Performance observations

The full native-suite run also records fake-native production EOS traffic over ten simulated seconds:

| Players / traffic | Host sent / received packets | Mean / peak sent packet | Peak gateway Poll |
| --- | --- | --- | --- |
| 2, original vehicle-only observation | 204 / 600 | 298.1 / 321 B | 0.168 ms |
| 8, original vehicle-only observation | 1,428 / 4,200 | 1,042.9 / 1,077 B | 2.924 ms |
| 2, reversed vehicle/prop messages | 404 / 600 | 252.0 / 321 B | 0.474 ms |
| 8, reversed vehicle/prop messages | 2,828 / 4,200 | 628.1 / 1,077 B | 16.044 ms |

These are observed process timing peaks in fake-native tests, not steady-state native EOS profiling or a frame-time guarantee. The eight-player mixed case's 16.044 ms peak is retained explicitly; its cause was not isolated. Real SDK Tick, RTT, loss and remote allocation cost are not measured by these tests. The tests verify ongoing publications and acknowledgements rather than enforce a timing threshold. Fixed window allocations and scans establish upper bounds by inspection; they do not prove negligible execution cost.

## Evidence boundaries

The regression and fault tests exercise the actual EOS gateway, not a replacement gateway or GNS impairment model. The native EOS SDK's separate-device packet delivery under those injected conditions remains unverified; the injection occurs at the fake native seam. Memory/processing bounds follow directly from fixed allocations and bounded loops; no native heap/soak profile is claimed.

Existing release 0.0.0.5 binaries and earlier two-PC LAN/four-PC Internet results predate this correction and **do not validate the corrected receiver**. No new release is published. Remote soak, loading investigations, reconnect/mid-game join features and historical GNS correction spikes remain outside this patch. Jira acceptance is not declared complete while access and physical verification limitations remain.

## Astra Self-Critique — Story Round 3

**Overall Score: 6.8 / 10. Quality Assessment: PASS (>=6.0).** This is the single authorized post-fix critique and the third Story round. It assesses the integrated result with the explicitly scoped correction; it does not create a fourth-round authorization or declare all physical acceptance criteria verified.

| Category | Score | Evidence and assessment |
| --- | --- | --- |
| Functionality / Integration | 7.0 | The exact independent-message failure is reproduced on the reviewed gateway and passes after the fix. Production vehicle, prop and acknowledgement state now advances with sustained reversal. |
| Networking | 7.0 | Transport-neutral unordered completion, reliable independence, duplicate rejection, wrap boundaries, nonce isolation, eviction and expiry are covered. The finite sequence window still intentionally drops sufficiently delayed unreliable messages. |
| Stability | 6.5 | Full builds, native checks, Godot sessions and local real EOS lifecycle checks pass. Corrected multi-PC official-SDK packet delivery and long-duration resource behavior remain unverified. |
| Performance | 6.0 | Memory/work have explicit finite bounds and no per-fragment assembly allocation. Fixed assembly memory increases to about 1.06 MiB per peer, and a 16.044 ms fake-native Poll peak remains unexplained; no steady-state performance guarantee follows. |
| Testing / Reliability | 7.0 | The former fragment-only coverage gap is closed with application-composed regression, interleaving, serial boundaries, expiry and 600-tick two/eight-player assertions. Deterministic injection cannot reproduce every real network/SDK condition. |
| Code Quality | 7.0 | The receiver policy is isolated in a small window class using the existing accumulator. Tombstones and modulo slots have straightforward bounds, but add state that must remain regression-tested. |
| Architecture | 8.0 | No application parsing in transport, Core change, SDK leakage, reliable snapshot workaround, new dependency or protocol abstraction. Ownership remains Client -> Core. |

### Useful recommendation

Before distributing a future corrected release, repeat the ordinary remote multi-PC gameplay check on a build containing this receiver and collect client snapshot/prop/acknowledgement progress. **No code change required; future physical verification, outside this patch's execution.** The earlier release cannot prove this fix works through the remote native SDK. Do not expand this correction into remote soak or loading work automatically.

### Delivery and acceptance

The final fetch confirmed `origin/main` remained `d6f39a4`; PR #15 remained open at `a6d0237` before local commit. This correction is committed locally on `ts-44-jg` for renewed human acceptance. It is not pushed, merged, published as a release or applied to another PR. Repository workflow requires renewed acceptance after post-acceptance implementation changes, and the user requested a stop after the completion report. TS-44/TS-48 Jira acceptance remains unasserted because the refresh failed and corrected remote physical verification remains outstanding.
