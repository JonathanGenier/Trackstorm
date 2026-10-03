# Astra Runtime Critique — Story Round 1

**TS-275 · branch `ts-275-pg` · 2026-10-02**

**Overall Score: 8.2 / 10**

**Quality Assessment: PASS (>=8.0)**

The exercised subsequent fall into the tunnel's inner dirt bank is stable and responsive. The car descends through contact, settles onto the tunnel floor, tolerates sustained steering/throttle pressure, and reverses away. I found no repeated long collision-processing stall in the independently executed contact traces, nor a visible launch, persistent suspension in midair, or unrecoverable wedge in the inspected fall sequence. This is a scoped assessment of the repaired fall/scrape, not a claim that the entire client has uniform frame pacing.

## Category Scores

| Material category | Score | Runtime basis |
| --- | ---: | --- |
| Runtime Functionality | 8.5 | Both straight approaches reach the lower face, settle and recover; the slower angled approach also reaches contact. |
| Controls / Responsiveness | 8.2 | Steering/throttle pressure stays bounded and reverse clears the original face. Judged through bounded input commands and traces, not physical-controller ergonomics. |
| Vehicle / Movement Feel | 8.2 | Airborne momentum, downward contact, landing and recovery form a coherent sequence in captures and tick histories. |
| Physics | 8.4 | The detailed fall continues downward after contact instead of hanging on the incline; six seconds of pressure remains bounded. |
| Observed Performance | 8.0 | Contact callback and sampled render timing remain short in the tested fall/pressure periods. Significant pre-contact startup intervals prevent a broader smoothness claim. |
| Runtime Stability | 8.5 | Commands completed and no crash occurred; normal-access repeat had empty stderr. Forced fixture termination does not verify graceful shutdown. |
| Runtime Integration | 8.1 | Bank contact, ground support, impact damage and subsequent driving operate together in the rendered hosted adapter. Broader regression preservation relies on the separately recorded final verification. |

Scores are experiential judgments, not a test-count average. Networking experience, audio, UI, general art quality and overall gameplay enjoyment are not independently scored by this narrow review.

## What Was Exercised

- **VERIFIED — independent execution:** launched the existing Godot 4.7.2 .NET `handling_playtest.tscn` with `--tunnel-scrape-playtest --handling-network`, Compatibility renderer on GTX 1070 at 1152×648. Issued throttle, steering, brake/reverse and spawn commands, inspected rendered screenshots with `view_image`, and examined recorded physics/timing traces. No production code was changed or rebuilt for this critique.
- **VERIFIED — six approach sequences:** both sides at 25 m/s straight; both sides at 12 m/s with ±0.15-radian offsets; both sides at 25 m/s with opposite ±0.15-radian offsets. Each sequence included 55 approach, 100 fall, 180 pressure and 180 reverse ticks: **3,090 ticks**. [Executed commands](astra-six-approaches-commands.ps1), [metrics](astra-six-approaches-metrics.json).
- **VERIFIED — lower-bank coverage:** the two straight fall segments registered 35 and 31 contact ticks, of which 21 and 20 followed downward velocity below -0.1 m/s. Endpoints were approximately (-6.582, 1.147, 14.685) and (6.586, 1.182, 14.543). The east 12 m/s angled approach registered 16 contact ticks. The opposite slow approach mostly missed; the two fast angled approaches stayed near the upper bank/bridge. Those three are adjacent coverage, not successful reproductions of the lower-bank fall.
- **VERIFIED — detailed normal-access repeat:** replayed the east 25 m/s line with five successive 20-tick fall captures, **360 pressure ticks (six seconds)** and 180 reverse ticks: **695 additional ticks**. Contact occurred at about (-7.100, 2.832, 14.867), followed by landing at about (-6.172, 1.099, 15.035). Pressure registered 328/360 contact ticks and ended near (-6.757, 1.148, 12.446). Reverse crossed away from that face and eventually contacted the opposite bank; it was not contact-free travel. [Commands](astra-detail-commands.ps1), [metrics](astra-detail-metrics.json), [runtime log](astra-runtime.log).
- **VERIFIED — rendered state:** inspected the airborne approach, both straight settled contacts, pressure and reverse results, and the detailed intermediate descent/landing. The car remains readable against the bank in the fixture camera. See [descending contact](astra-east-fall-2.png), [landing](astra-east-fall-3.png), [sustained pressure](astra-east-pressure.png), [reverse result](astra-east-reverse.png).
- **VERIFIED — timing:** across the first run's 12 fall/pressure segments, the largest per-segment observation mean was approximately **3.69 ms**, p95 **4.05 ms**, and maximum **14.24 ms**; largest sampled render p95 was **9.81 ms**, maximum **23.13 ms**. In the normal-access detailed repeat, fall chunks had callback maxima no higher than **4.78 ms** and sampled render maxima no higher than **7.00 ms**. Six-second pressure measured callback mean **1.20 ms**, p95 **1.66 ms**, maximum **14.24 ms**, sampled render p95 **7.00 ms**, maximum **21.89 ms**. No repeated long contact-processing stall appeared in these samples.
- **INFERRED — broader preservation:** the final verification report supplies actual executed terrain/rock, collision, landing, recovery, death/respawn and separate-process network evidence. I read the report and the cleaned-up bank-UDP final log, which passes all three speeds under 30 ms delay, 5 ms jitter and 2% loss. Those are supporting integration evidence, not additional firsthand gameplay or peer observation by this reviewer. The rendered `--handling-network` adapter is not itself a multiplayer session.

## Runtime / Operational Limitations

- The first launch used the sandbox and emitted user-log write and root-certificate-store errors ([stderr](astra-sandbox-errors.log)). A subsequent normal-access launch completed the detailed repeat with empty stderr. The sandbox diagnostics do not establish a production defect.
- Startup pacing remains visibly measurable outside the target contact: the first run recorded **1,506.684 ms** at tick 20 with zero contacts and a 0.477 ms observation callback. The normal-access repeat also recorded a **429.94 ms** pre-contact interval. Their cause was not established, and normal access did not eliminate startup pauses. No whole-session hitch-free claim is warranted; these events are not evidence of the subsequent falling-bank collision hitch.
- Render intervals are `_Process` callback intervals sampled at physics ticks, not a complete unique display-frame capture. Screenshots plus trace sequences support the judgment; no continuous video viewing, audio listening, human controller session or latency ergonomics assessment was performed.
- One hosted vehicle was directly exercised. Other-player contact, prediction/reconciliation, death/respawn, rock impacts, rear shield, pillars and perimeter were covered by the implementation's final verification, not independently re-played here. No Internet/EOS, remote machine, exported build or long-duration soak was exercised by this reviewer.
- The original user's exact video/input path is unavailable. The map-based approach reproduces falling inner-bank contact, but identity with that original line remains unverified. The sampled contacts cannot establish universal performance or collision equivalence at every pose.
- Both fixture processes were intentionally stopped after completed commands, so this critique does not certify shutdown cleanup. No source-code engineering review was performed.

The formal critique is complete. Stop for the human decision; this PASS does not authorize another round or additional implementation.
