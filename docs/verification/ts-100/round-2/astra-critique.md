# Astra Runtime Critique — Story Round 2

**Overall Score: 8.1 / 10**

**Quality Assessment: PASS (>=8.0)**

Independent runtime assessment of TS-100 on `24d5287`, after the authorized corrections and latest-main integration. The overall score is a holistic judgment of this Story's environment and operational result, not an arithmetic average or a reward for implementation effort or check count.

The corrected environment reads coherently at driving height and at overview scale. The grass keeps restrained surface detail without the previous broad blurred bands or conspicuous aerial mottling. Distinct tree crowns, mixed heights, gaps and irregular depth break the former concentric rows and repeated spiky wall. The visible perimeter remains legible, the infield stays open, and scenery does not obscure the usable road in the inspected driving views. Fresh runtime execution preserved stable bank/infield traversal, pickup acquisition and containment. Together these resolve the material presentation objections from Round 1 within the assigned scope.

## Category Scores

| Runtime category | Score |
| --- | ---: |
| Runtime Functionality | 8.8 |
| Physics | 8.5 |
| Multiplayer / Networking Experience | 7.8 |
| Camera | 8.0 |
| Visual Quality | 8.0 |
| Observed Performance | 7.8 |
| Runtime Stability | 8.5 |
| Runtime Integration | 8.3 |

Networking and performance remain below the other categories because occasional stalls and correction outliers still exist in the local eight-process telemetry. The passing Story score does not characterize that workload as uniformly smooth or establish network quality beyond the measured environment.

## What Was Exercised

- **VERIFIED — independently executed:** fresh `check-oval.ps1 -Visual -NoBuild` completed all 258 checks with exit code zero and no runtime warning/error, using Godot 4.7.2 .NET, Compatibility renderer and GTX 1070. This exercised three automated high-speed laps, banking and infield transitions through both vehicle adapters, 32 high-speed impact/launch scenarios, all twenty production-vehicle pickup approaches, eight-car practice/reset, road/perimeter queries and exterior scenery clearance. [Runtime log](astra-oval.log), [measured assertions](astra-oval-evidence.txt). Native synthetic inputs were used; this was not manual driving.
- **VERIFIED — directly viewed:** fresh [overview](astra-overview.png), [chase](astra-chase.png), [banked vehicle](astra-banked.png), [banking/scenery](astra-banking.png) and [practice](astra-practice.png) captures. Also inspected the implementation's [grass close view](grass.png), [integrated pickup view](integrated-pickups.png) and [integrated network camera view](integrated-network-camera.png). These are actual rendered captures, not an independently watched continuous race.
- **VERIFIED — independently executed:** fresh ordinary eight-car idle practice sample, 180 warm-up frames plus 600 measured rendered frames: mean **6.985 ms**, p95 **9.962 ms**, maximum **30.713 ms**, 528 final-sample draw calls and 656 rendered objects. Process exited cleanly. [Log](astra-performance.log). This supports comfortable rendering of the stationary scene on the tested machine; it is not a combat stress result.
- **Recorded runtime evidence reviewed, not independently rerun:** synchronized eight-peer native pickup contention, all four item types, cooldown and occupied-slot protection; three-peer migration; three reconnect resyncs including 125 seconds offline. Reviewed their clean logs and the integrated ordinary eight-peer raw JSON in `.godot/network-vehicle-checks/2730040cb6d64d13888580b637374aec` (retained in [integrated-eight](integrated-eight/)). Every client p99 is below the unchanged 3 m criterion: rendered peer **1.048 m**, largest client p99 **2.080 m**. The rendered peer maximum error is **4.157 m**, maximum frame gap **447.215 ms**, and host maximum frame gap **905.292 ms**. Passing p99 does not erase those outliers.
- **INFERRED from the reviewed controlled runs:** scheduling pressure is a well-supported explanation for the reproduced original-scene failure. Original scenery with ordinary scheduling failed at rendered p99 **3.380 m** and a **2267 ms** host frame gap; isolating the host made that same scenery pass at p99 **0.19 m** and about **86 ms** host maximum frame gap without relaxing prediction thresholds. Repeated ordinary refined-scene passes provide relevant acceptance evidence in addition to that diagnostic control. These observations support a scheduling explanation; they do not prove the exact cause of every historical failure or isolate CPU execution cost from descheduling.

## Runtime / Operational Limitations

No physical keyboard/controller playtest, eight-human race, listening assessment, WAN/NAT, multi-machine test, authenticated EOS session, packaged export, prolonged combat soak or hardware-fleet benchmark was independently performed. Fun, human control feel and audio therefore receive no score. Migration, reconnect and eight-peer networking conclusions rely on reviewed primary runtime evidence rather than a second independent execution. The unrelated `EosLobbyProvider.Read()` exception was excluded from this assignment and is not claimed resolved. Containment conclusions apply to the exercised envelope, not arbitrary teleports or unlimited impulses.

This critique evaluates the resulting runtime experience; it does not replace the separate engineering review. **STOP for explicit human decision**, as required by [the critique policy](../../../critique.md). No further corrective round or implementation change is authorized by this passing score.
