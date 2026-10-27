# TS-245 Round 6 runtime evidence

Actual Godot 4.7.2 Compatibility captures on a GTX 1070, 1280×720. Automated native driving, local UDP peers and synthetic boundary poses supplement direct image observation; none of this is human playtesting. The [Story report](../../ts-245.md) records scope, results, failures and limitations.

## Presentation and motion

- [Production gameplay camera: tank lettering and full meter](gameplay-meter-full.png), [firing](gameplay-stage-1-firing.png), [airborne firing](gameplay-stage-3-firing.png).
- [Gameplay-camera driving sequence](gameplay-driving.webp), [chronological sheet](gameplay-driving-sequence.png).
- [Deployment / straight burst / bump / braking](deployment-straight.webp), [chronological sheet](deployment-straight-sequence.png).
- [Airborne burst and deposition](airborne.webp), [chronological sheet](airborne-sequence.png).
- Finer burst samples: [straight](firing-1-sequence.png), [turning](firing-2-sequence.png), [airborne](firing-3-sequence.png), [replacement burst](firing-5-sequence.png); [original-frame hashes](firing-sequences.json).
- [Meter drain, mouth closure, retraction and replacement](replacement.webp), [chronological sheet](replacement-sequence.png).
- [Tank](tank-detail.png), [jester and hoses](jester-detail.png), [firing](stage-1-firing.png), [rounded impact splashes](impact.png), [mouth closing](mouth-closing.png).
- [Full](meter-full.png), [partial](meter-half.png), [empty](meter-empty.png) gauge. `half` is a filename, not an assertion of an exact 50% readback; the actual displayed fraction is in the log.

Animated WebP sequences use captured frame spacing at 60 Hz, **not measured wall-clock video**. Contact sheets only resize and arrange genuine captures. [Source-frame hashes](recordings.json) identify every input image. No generated concept imagery substitutes for runtime evidence.

## Clearance and surface support

- [Maximum backward lean, default stream width](clearance-clearance-0.24-20.png), [another boundary pose](clearance-clearance-0.24-80.png), [maximum stream width](clearance-clearance-0.80-20.png).
- [780-pose result including hardware-occluded quads](clearance-evidence.txt), [native log](clearance-runtime.log).
- [Original backward-lean failure](before-boundary-clearance-20.png), [original boundary log](before-boundary-runtime.log), [moving-sample intersection before correction](before-moving-box-intersection.png), [moving failure log](before-moving-runtime.log).
- [Grass → bank, first firing timing](bank-0.webp), [second timing / hard reversal](bank-1.webp), [airborne approach](bank-2.webp).
- [Deposits after first](bank-bank-0-deposits.png), [second](bank-bank-1-deposits.png), [airborne](bank-bank-2-deposits.png) activation. Gray angular tire trails are separate from black Oil geometry.
- [Final bank support evidence](bank-evidence.txt), [trace](bank-trace.json), [log](bank-runtime.log). [Earlier failed support/turning diagnostic](bank-diagnostic.log) is retained honestly.

## Lifecycle, networking and cost

- [Full four-peer runtime evidence](evidence.txt), [log](runtime.log), [trace](trace.json), [measured repeated-use summary](runtime-summary.json).
- [Gameplay-camera run](gameplay-runtime.log) and [evidence](gameplay-evidence.txt).
- [Expiry fade](expired-fading.png), [repeated-use deposits](repeated-use.png), [curved trail](curved-deposits.png), [airborne deposits](airborne-deposits.png), [vertical impact](wall-deposits.png).
- [Failed early-Welcome admission attempt](admission-failure.log); the complete unchanged-build retry passed. A successful retry does not resolve the intermittent admission risk.
- [Repository gate](check-final.log), [focused ballistic/coverage tests](core-final.log), [asset/anchor/hash validation](asset-validation.json).
- [Contacts](contacts-final.log), [handling](handling-final.log), [482 rack handoffs](rack-final.log), [Oil developer options](developer-options-final.log), [startup](startup-final.log).
- Shared recovery failures: [reconnect first attempt](reconnect-final.log), [three-peer migration first attempt](migration-final.log), [two-peer migration first attempt](migration-two-peer.log). Separate retries: [reconnect](reconnect-retry.log), [three-peer migration](migration-retry.log), [two-peer migration](migration-two-peer-retry.log). The Story report distinguishes outcomes and unresolved limits.
- [Blender generation](blender-build.log), [Godot import](import.log).

## Independent formal critique

[Astra Story Round 6: **7.8 / 10 — FAIL**](astra-round-6.md). The independent fresh native presentation run passed in 118.21 seconds with three peers; it did not repeat expiry or sustained use. The critique records remaining fluid silhouettes and current unresolved recovery failures, and stops for human direction.

- [Fresh native log](astra-native-runtime.log), [evidence](astra-native-evidence.txt), [trace](astra-native-trace.json), [capture/log hashes](astra-native-sources.json).
- Fifty directly inspected chronological frames: [deployment](astra-sequence-deployment.png), [straight burst](astra-sequence-straight.png), [airborne](astra-sequence-airborne.png), [replacement](astra-sequence-replacement.png); [original-frame hashes](astra-sequence-sources.json).
- [Fresh firing](astra-native-stage-1-firing.png), [airborne tail](astra-native-stage-3-firing.png), [impact splashes](astra-native-impact.png), [tank](astra-native-tank-detail.png), [jester/hoses](astra-native-jester-detail.png).
- Fresh gauge captures: [full](astra-native-meter-full.png), [partial (0.426)](astra-native-meter-half.png), [empty](astra-native-meter-empty.png); [mouth closing before retraction](astra-native-mouth-closing.png).

The mesh/trajectory checks are sampled, conservative presentation bounds can shorten older strands during a reversal, and terrain support is sampled rather than a mathematical guarantee for arbitrary geometry. The four-scene forced-render timings do not establish normal gameplay FPS, release performance or a long-session soak. Remote viewport appearance, WAN/EOS, physical controls and human acceptance remain unverified.
