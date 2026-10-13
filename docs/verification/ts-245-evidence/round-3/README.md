# TS-245 Round 3 runtime evidence

These are fresh Godot 4.7.2 .NET Compatibility renderer captures on the local GTX 1070. The host viewport is 1280×720; three other local UDP scenes retain production physics, replication and presentation with drawing disabled. They are automated native observations, not a human playtest pass or concept renders.

[Formal Astra Story Round 3 critique](astra-round-3.md): **7.7 / 10 — FAIL (<8.0)**. The independent fresh native run passed; the remaining material findings concern cord-like/folded discharge and synthetic deposit/splash appearance. Reviewer evidence is prefixed `astra-native-`. No further polish was performed after the critique; human direction is required.

## Gameplay presentation

- [Closed box](closed.png), [deployed assembly](ready.png) and [jester face/hat](jester-detail.png) show the white/red/brass box, white smiling face, connected black/red hat, gold bells and flatter collar in gameplay. The close-view filename does not imply an isolated asset preview.
- [Straight firing](stage-1-firing.png), [turning firing](stage-2-firing.png), [airborne firing](stage-3-firing.png), [wall firing](stage-4-firing.png) and [impact](impact.png) show the one-second multi-strand burst and its deposition. Readback occurs after the requested observation point, so filenames do not establish an exact screenshot tick.
- [Straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png), [airborne deposits](airborne-deposits.png) and [vertical deposits](wall-deposits.png) use cameras aimed at actual impact geometry. [Replacement](replacement-ready.png) and [repeated use](repeated-use.png) retain the surrounding rack/vehicle context.

## Temporal evidence

[Deployment, inertia and straight firing](deployment-straight.webp), [turning/drifting](turning.webp), [airborne burst and landing](airborne.webp), and [retraction/replacement](replacement.webp) are WebP sequences encoded from unretouched native motion captures. Playback durations use the **60 Hz simulation frame spacing**, not measured wall-clock performance. Frames can be sparse when native drawing/readback is slow. [The recording manifest](recordings.json) retains source frame numbers and SHA-256 hashes.

## Results and limits

[Native run log](runtime.log), [asserted evidence](evidence.txt), [sampled trace](trace.json) and [runtime summary](runtime-summary.json) retain the full four-peer impaired run. It exercised forty parcels in sixty ticks, straight/braked/drifting/airborne trajectories, a supported asphalt ramp query, a vertical wall, replacement, active/late admission, exact replicated Oil state, twelve subsequent host/remote uses and complete cleanup. Activation 1844 expired at tick 3644, first absent at 3644. The final assertions require zero Oil visual owners on all four peers.

The repeated-use stage explicitly forced drawing even when Windows minimized the window. Its 386 sampled render intervals had median **28.197 ms**, p95 **63.459 ms**, maximum **101.403 ms**. At most six hazards overlapped; the observed hazard peaked at forty flying parcels and 59 additional pieces. Forced rendering can draw twice when visible, and other applications remained open. These are fixture observations, not controlled GPU timings or a 60 FPS guarantee.

[Contacts](contacts.log), [handling](handling.log), [rack](rack.log), [reconnect](reconnect.log), [migration](migration.log), [repository gate](check.log), [startup](startup.log), [coverage comparison](coverage.log) and [asset validation](asset-validation.json) retain the corresponding checks. The comparison preserves forty parcels and comparable sampled area; it does not prove identical coverage or balance. Selected box dimensions/head anchors are unchanged; decorative geometry is intentionally revised.

[An earlier admission timeout](earlier-admission-failure.log) occurred while other native checks ran concurrently. One airborne-joining peer never reached the accepted cleanup state. The complete isolated rerun passed after adding timeout diagnostics, with no admission logic or assertion changes. Its cause remains undetermined.

Human acceptance, manual control feel, all tuning extremes, exhaustive mesh clearance, a driven ramp crest, authenticated EOS/WAN/multi-device operation, release-export performance and long-session soak remain unverified. See the [Story report](../../ts-245.md) for iteration failures and the formal critique outcome.

[Twenty handling trial summaries](handling-summary.json) retain wheel masks, controls, speeds and recovery observations; the native handling stdout alone only records startup. [Fractional-duration budget regression](fractional-budget.log) verifies that developer settings are validated after rounding firing duration to simulation ticks.
