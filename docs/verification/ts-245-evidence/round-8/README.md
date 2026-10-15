# TS-245 Round 8 tank-art evidence

Actual Godot 4.7.2 Compatibility rendering on a GTX 1070 at 1280×720. Native scripted driving and capture inspection are automated evidence, not human playtesting. See the [Story report](../../ts-245.md) for actual results and limitations.

## Rear-camera artwork and branding

- [Final gameplay rear view](gameplay-meter-full.png), [firing at gameplay distance](gameplay-stage-1-firing.png), [airborne gameplay view](gameplay-stage-3-firing.png).
- Chronological driving: [straight/braking](gameplay-driving.webp), [turning](gameplay-turning.webp), [airborne](gameplay-airborne.webp), [meter/replacement](gameplay-meter-replacement.webp).
- [Close painted barrel and full gauge](meter-full.png), [partial gauge](meter-half.png), [empty gauge](meter-empty.png), [tank in the assembly](tank-detail.png).
- [Previous round's rear view](../round-7/gameplay-meter-full.png) and [this round before the correction's contrast adjustment](iteration-tank-before-contrast.png). The first iteration and final logs are retained separately: [first](gameplay-first.log), [final](gameplay-final.log).

The artwork restores visible harlequin diamonds around the painted wordmark, with ivory scalloping, ochre outlines/flourishes, shoulder pinstripes and stencil stars. The red oil correction has its own joined ivory brush area. The concept images remain reference-only and are not redistributed here.

## Mechanism, deposition and repeated use

- [Full four-peer evidence](evidence.txt), [runtime log](runtime.log), [trace](trace.json), [repeated-use measurements](runtime-summary.json).
- [Deployment/straight firing](deployment-straight.webp), [airborne firing](airborne.webp), [retraction/replacement](replacement.webp).
- [Straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png), [airborne deposits](airborne-deposits.png), [expiry fade](expired-fading.png), [repeated use](repeated-use.png).
- [780-pose clearance measurements](clearance-evidence.txt), [clearance log](clearance-final.log).
- [Production-bank measurements](bank-evidence.txt), [bank log](bank-final.log), [bank trace](bank-trace.json), [hard-drift sequence](bank-1.webp), [supported deposits](bank-bank-1-deposits.png).
- [Contacts/scoring](contacts-final.log), [native handling](handling-final.log), [firing reconnect](oil-reconnect-final.log), [rack handoffs](rack-final.log), [startup](startup-final.log), [native result manifest](native-status.json).

Animated WebP spacing uses captured **60 Hz simulation-frame numbers**, not wall-clock video. Contact sheets arrange genuine frames. [Source frame names and hashes](recordings.json) identify the inputs; these recordings do not establish live audiovisual smoothness or human control feel.

## Build and asset preservation

- [Final repository gate](check-final.log), [targeted Client build](client-fast.log), [Blender rebuild](blender-final.log), [native Godot import](import-final.log).
- [Asset hashes and geometry comparison](asset-validation.json). Vertex/morph positions and oriented topology are exact; small normal/UV export rounding is reported explicitly. No texture-dimension, mesh-count or material-count increase.

Text logs use repository LF line endings; incidental trailing whitespace is normalized without changing diagnostic content. The previous round's shared migration continuation failure and intermittent rack shutdown warning remain [historical evidence](../round-7/README.md), not newly run or repaired migration outcomes.

## Formal critique

[Astra Runtime Critique — Story Round 8](astra-round-8.md): **7.9 / 10, FAIL** overall; **8.5 / 10** Visual Quality. The tank-art improvement succeeds, while unchanged pointed liquid fragments and historical shared migration continuation prevent an integrated pass. No further correction was implemented.

The reviewer independently ran the native impaired gameplay-camera fixture: [log](astra-native-runtime.log), [measurements](astra-native-evidence.txt), [trace](astra-native-trace.json), [rear view](astra-native-meter-full.png), [driving](astra-sequence-driving.png), [turning](astra-sequence-turning.png), [airborne](astra-sequence-airborne.png) and [replacement](astra-sequence-replacement.png). [Provenance](astra-native-sources.json) separates raw source hashes from normalized retained copies. The fresh run passed; shortened VisualOnly coverage does not independently repeat expiry, sustained use or reconnect. See the critique for exact observation limits and recommendations subject to human approval.
