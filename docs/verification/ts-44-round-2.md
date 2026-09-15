# TS-44 correction round 2

Authorized scope: investigate/fix the two round-1 recommendations and package Windows version 0.0.0.5. Branch: `ts-44-jg`. `origin/main` was fetched before work; no merge or PR was performed.

## Corrections

1. **Vehicle send/closure race.** Reproduced `Transport send failed: NoConnection` in the eight-process Godot scenario. A connection can close natively after Poll but before publication. Vehicle sends now follow the existing lobby recovery pattern: disconnect that peer, let the next host boundary remove its vehicle, and preserve the host simulation for remaining peers. Clients receive a recoverable failure. Snapshot, prop, welcome, item and input sends use the same path. A deterministic fake-native closure regression proves the host advances without an exception and removes the peer.
2. **Persistent control backlog.** Bounded correction traces recorded approximately 78–84 outstanding commands around large corrections, over one second of replay at 60 Hz. The host previously consumed one queued command each tick without retiring excess backlog; once a burst accumulated, equal input/consumption rates preserved that delay. Core now retires excess older queued commands before stepping, retaining the newest six (100 ms). Acknowledgements explicitly retire those commands; the host still simulates exactly one tick, so catch-up never grants extra movement ticks. Two deterministic burst tests cover normal and wrapping sequence origins. This bounds queued control delay; it cannot eliminate network propagation delay or all collision corrections.
3. **Real-device EOS verification.** Added an opt-in production-composition check and launcher. Separate PCs use real device identities, EOS lobby discovery, native P2P, lobby Ready/Start, a 20-second vehicle arena, Return acknowledgement and cleanup. The result records success/failure, client snapshots, packet/byte/reliability totals, peak gateway Poll and SDK Tick costs separately, and correction metrics. No credentials or raw user identities are recorded. Automated discovery has a 15-minute deadline. This makes remote verification executable and observable; it does not itself prove that the remote run passed.
4. **Release.** Exported version 0.0.0.5 to `C:\Users\j_gen\Documents\Game Projects\Releases\Trackstorm\Trackstorm_0.0.0.5`. The ZIP contains the executable, runtime data/binaries/notices, PowerShell launcher, double-click client launcher and tester instructions. Windows file/product versions both read 0.0.0.5.

## Verification

- **VERIFIED PASS:** `check.ps1`, formatting, Debug/Release warnings-as-errors builds, 194 Core tests and 81 non-native transport tests per configuration, repeated after tightening the remote completion guard and canonical name handling. Final C# evidence: `.godot/ts44-r2-canonical-check.log`.
- **VERIFIED PASS:** targeted Core replication suite (17 cases) and vehicle driver suite (6 cases), including backlog/wrap and send-closure regressions.
- **VERIFIED PASS:** complete native suite, 92/92 tests plus three Godot transport lifecycle cycles; eight-player lobby/arena/Return/departure regression. Local evidence: `.godot/ts44-r2-native.log`, `.godot/ts44-r2-lobby.log`.
- **VERIFIED FAIL before corrections:** the original impaired arena scenario reproduced p99 failure (4.4938 m, maximum 28.7078 m) and another run reproduced native send closure. Local evidence: `.godot/ts44-r2-stress-before.log`, `.godot/ts44-r2-stress-trace.log`.
- **VERIFIED PASS after corrections, twice:** eight actual Godot processes, 30 ms latency, 10 ms jitter, 2% loss. All peers met the unchanged runtime thresholds. Maximum client p99 was 2.38 m in run 1 and 1.59 m in run 2. Local evidence: `.godot/ts44-r2-stress-fixed1.log`, `.godot/ts44-r2-stress-fixed2.log`; artifacts `5839d16671e84f7cb97e117ed09b4965` and `c1196c77f72a4fbda878d0911c5d3405` under `.godot/network-vehicle-checks`.
- **LIMITATION:** isolated large corrections remain (maximum 9.80 m in run 2), although p99 passes. Two local runs are evidence of improvement, not a guarantee across arbitrary load or networks. These impairment measurements use GNS and do not establish EOS Internet performance.
- **VERIFIED:** exported executable starts locally as an authenticated test host; packaging includes the existing EOS runtime. Final ZIP SHA-256: `3FCDD92948C785C029C79BAED373DB6AD65C6A479836B9C4370A98550CA062D0`.
- **VERIFIED launcher correction:** the tester reported an illegal output path. The initial batch launcher quoted a trailing directory backslash, and Windows PowerShell 5 evaluated the script-root parameter default before its value was available. The launcher now omits that argument, and the script resolves its fallback in the body. A real Windows PowerShell 5 / batch probe with a spaced directory resolves both executable and output paths correctly. Updated script and launcher are included in the final ZIP.
- **VERIFIED discovery correction:** production lobby names discard dots. The original automated client compared its dotted test name against the canonical published name and waited indefinitely. The harness now applies the production sanitizer before creation/search, and the launcher default uses `Trackstorm 0005 remote check`. The successful real-device run used that canonical name explicitly.
- **VERIFIED exit-check correction:** Windows PowerShell 5 returned null for the exit code of a redirected process. Reproduced locally with a successful native process; retaining the process handle before waiting restored the code. The corrected launcher recognizes 0 and rejects 7, prints the actual native code, and still rejects a missing code. See the [official PowerShell issue](https://github.com/PowerShell/PowerShell/issues/5421). This corrects a false failure report without ignoring genuine nonzero process exits.

## EOS evidence boundary

**Two-PC EOS gameplay passed on the same router/network**, as identified by the tester. Host evidence was read directly from `.godot/ts44-r2-remote-host-2/eos-host-result.json`; the tester pasted the client result and confirmed its error log was empty. Both report `Passed: true`, Stage 4 and completed discovery/connect/Ready/Start/arena/Return/leave through real EOS packets. The host process exit was 0. The client's original wrapper exit check reported failure because its Windows PowerShell 5 code was unavailable; the exact launcher defect was independently reproduced and fixed as above. The original client native exit code itself was not recovered.

| Measurement | Host | Client (tester-supplied output) |
| --- | --- | --- |
| Arena duration | 20.000 s | 19.983 s |
| Authoritative snapshots received | N/A | 399 |
| Sent / received datagrams | 810 / 1,203 | 1,205 / 810 |
| Reliable sent datagrams | 10 | 7 |
| Sent bytes | 244,248 | 153,555 |
| Peak sent datagram | 424 B | 143 B |
| Peak gateway Poll | 8.6271 ms | 1.5913 ms |
| Peak SDK Tick | 20.7077 ms | 7.4184 ms |
| Correction p99 / maximum | N/A | 0.000117 m / 0.040947 m |

Counters and peak timings span the whole process/test, including admission and startup; the host waited about 529 seconds overall for the tester, so its total duration must not be used as an arena packet-rate denominator. Peak times include cold initialization and were not isolated steady-state profiles. The 399 client snapshots over 19.983 arena seconds are approximately 20 received snapshots/s. The host consumed about 1,200 client datagrams during gameplay, consistent with 60 Hz inputs; handshake/control packets are included in the cumulative counters. EOS sampled RTT and route remain unavailable.

This two-PC run establishes local-network interoperability. The later four-PC run below adds Internet evidence. Repeated real solo host lifecycle tests and fake peer lifecycle tests remain recorded in round 1; repeated cycles with the same remote four-PC roster have not yet been recorded.

## Subsequent four-PC Internet verification — 2026-09-14

**VERIFIED host result:** release 0.0.0.5 hosted `Trackstorm monitored test` with `-Players 4`: one automated host and three human clients playing normally. The host completed discovery/connect/Ready/Start, 20 seconds in the arena, Return acknowledgements and leave. Its result reports `Passed: true`, Stage 4 and four players. The monitored launcher returned native exit code 0; standard error was empty. Local evidence: `.godot/ts44-four-player-monitored/eos-host-result.json`, `eos-host.log` and `eos-host-errors.log`.

**Tester-reported topology and experience:** the user confirmed that all three human clients used separate Internet connections and that nobody appeared to lag. One client took longer to load into the game. Client loading durations, hardware, frame times and connection setup timings were not collected, so the cause of that delay is unknown. No claim about client correction statistics follows from the host's zero-valued correction fields.

| Host measurement | Value |
| --- | --- |
| Arena / whole test duration | 20.000 s / 191.833 s |
| Sent / received datagrams | 2,445 / 3,220 |
| Reliable / unreliable sent datagrams | 45 / 2,400 |
| Sent bytes | 1,005,402 |
| Mean / peak sent datagram | 411.2 B / 725 B |
| Peak gateway Poll | 7.0953 ms |
| Peak SDK Tick | 15.9003 ms |
| Client snapshots / corrections | Not collected from human clients |
| RTT / route | Unavailable |

Counters cover the full test, including control traffic and waiting. They are not isolated arena packet-rate measurements. Peak costs include startup and do not establish steady-state frame time. The successful short session does not establish long-duration stability.

### Current acceptance coverage

| Requirement | Current evidence and boundary |
| --- | --- |
| EOS gameplay across separate Internet connections | Four-PC flow passed on the host; the three human clients' separate connections were confirmed by the user. Residential connection type and NAT topology were not measured. |
| Four/five independent PCs | Four PCs exercised: three human clients and one automated host. This is not evidence of four/five human testers or a five-PC run. |
| Discovery, admission, Ready/Start, arena, Return and leave | Completed through real EOS in the two-PC LAN and four-PC Internet runs. |
| Reliable lobby and unreliable vehicle transport | Real peer traffic and completed control/gameplay flow verified; four-PC host totals recorded above. |
| Normal flow without IP or router configuration | Production EOS browser flow requires no IP or port. Human clients joined the named lobby; router settings were not independently inspected. |
| Capacity, bounds, stale traffic and cleanup | Automated gateway/driver coverage and local runtime evidence remain applicable. Four-player Internet completion verifies one remote cleanup cycle, not a leak soak. |
| Direct-IP regression and impaired vehicle behavior | Native suite and local production lobby checks passed; eight-process impairment scenario passed twice after corrections. Physical multi-PC Direct-IP LAN testing remains unverified. |
| Windows dependencies on remote PCs | Release ran successfully for three human clients. Clean-machine dependency inventories were not collected. |
| Remaining physical measurements | Repeated four-PC cycles, long-duration load, eight-PC EOS performance, CGNAT/double-NAT/proxy paths and human-client correction/frame-time measurements remain unverified. |

### Delivery status

The last integrated critique remains Story Round 2, **6.3/10 PASS**; this section adds physical verification evidence and does not constitute another critique or waive unmet acceptance requirements. On resumption, `origin/main` was fetched and remained at `d6f39a4`, already an ancestor of this Story branch. Jira refresh returned an access error (app not installed); this coverage uses the complete requirements read earlier and their recorded mapping. No game code or release binary changed during this evidence update, so the previously completed build/runtime checks and ZIP hash still apply. Final Story acceptance and PR creation remain subject to the repository's explicit human acceptance gate.

Official EOS SDK 1.19.1.2 generated `P2PInterface.AcceptConnection` documentation was inspected: explicit accept/request initiates peer notification, the remote must subscribe and accept, and establishment is reported when packet communication is ready. `SendPacketOptions.DisableAutoAcceptConnection` requires explicit acceptance. The current adapter follows that documented sequence. The official web P2P page was requested but returned no readable content; no third-party search result was used as implementation authority.
