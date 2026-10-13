# TS-245 Round 4 runtime evidence

These are Godot 4.7.2 .NET Compatibility renderer captures on the local GTX 1070. The host viewport is 1280×720; three other local UDP scenes retain production physics, replication and presentation with drawing disabled. They are automated native observations, not a human playtest pass or concept renders.

Retained text logs normalize line endings and trailing whitespace only; messages and results are unchanged.

## Gameplay presentation

- [Closed box](closed.png), [deployed assembly](ready.png), [tank view](tank-detail.png) and [jester view](jester-detail.png) show the original rack position, horizontal tank, white/red/brass materials and two flexible hoses. The decorative gauge does not claim a gameplay capacity state.
- [Tank close-up](tank-detail-earlier-closeup.png) and [jester close-up](jester-detail-earlier-closeup.png) are from an earlier capture iteration of the **same unchanged model/material build**. They establish appearance and lettering only. They are not used to establish precise animation timing or a moving stream connection.
- [Straight firing](stage-1-firing.png), [turning firing](stage-2-firing.png), [airborne firing](stage-3-firing.png), [wall firing](stage-4-firing.png), [closer stationary firing](stage-5-firing.png), [impact](impact.png), and [mouth closing](mouth-closing.png) show the one-second discharge and sequence. Each firing readback checked the actual liquid mesh's first world-space vertex against the animated mouth: approximately 0.007 m separation. Named screenshots follow the requested observation boundary and do not establish an exact screenshot tick.
- [Straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png), [airborne deposits](airborne-deposits.png), and [vertical deposits](wall-deposits.png) aim at actual contacted geometry. [Expired visual residue](expired-fading.png) shows breakup after gameplay has ended. [Replacement](replacement-ready.png) and [repeated use](repeated-use.png) retain surrounding vehicle context.

## Temporal evidence

[Deployment, inertia and straight firing](deployment-straight.webp), [turning/drifting](turning.webp), [airborne burst and landing](airborne.webp), and [retraction/replacement](replacement.webp) are sequences encoded from unretouched native motion captures. Playback durations use **60 Hz simulation frame spacing**, not measured wall-clock performance. Readback/encoding makes sampling sparse. [The recording manifest](recordings.json) retains source frame numbers and SHA-256 hashes.

## Results and limits

[Native log](runtime.log), [asserted evidence](evidence.txt), [sampled trace](trace.json) and [summary](runtime-summary.json) retain the full four-peer impaired run: 30 ms delay, 5 ms jitter and 2% packet loss. It exercised sixty-tick/forty-parcel emission, acceleration/braking/reacceleration, supported turning/drift, airborne firing and landing, a supported 23-degree asphalt ramp query, vertical impact, mouth closure before retraction, hose endpoints, pickup replacement, active/late admission, exact replicated Oil state and twelve subsequent host/remote uses. Activation **1817** expired and was first absent at **3617**. Gameplay ended before visual dissipation. All four peers reached zero hazards and zero Oil visual owners.

Rendered spring peaks were fore/aft **0.446 m**, lateral **0.428 m**, vertical **0.450 m**. These observed bounds and nesting assertions do not prove exhaustive swept-mesh clearance at every tuning extreme.

Repeated use sampled median **42.064 ms**, p95 **87.409 ms**, maximum **134.925 ms** render intervals, with at most six hazards. Forty in-flight parcels and 59 additional surface segments were the observed maxima. Static memory peaked at 186,695,397 bytes during this stage and ended at 176,003,879 bytes with the four arenas still loaded. Four scenes, forced drawing, capture overhead and other local applications prevent a controlled benchmark or a 60 FPS claim. These measurements should not be interpreted as single-client GPU timings.

[Contacts](contacts.log), [twenty handling trials](handling-summary.json), [handling log](handling.log), [rack](rack.log), [reconnect](reconnect.log), [two-peer migration](migration-two-peers.log), [Oil developer options](developer-options-oil.log), [repository gate](check.log), [startup](startup.log), [trajectory/coverage tests](trajectory.log), and [asset validation](asset-validation.json) retain passing outcomes. Oil developer options passed 112 write-phase assertions and thirteen fresh-process checks. See the [Story report](../../ts-245.md) for final results and limitations.

## Investigation evidence

Initial capture iterations read pixels inside `_Process`. The geometry assertion showed a connected stream while images combined new world-space vertices with a previous rendered car pose. Forcing spatial transforms alone was insufficient. Deferring readback until the next process boundary produced coherent moving-car/stream captures. Earlier mislabeled or mismatched fluid captures are not retained as final visual proof.

[The first three-peer migration attempt](migration-first-failure.log) and [isolated retry](migration.log) failed at stage 61 while establishing the replacement-host authority fence, before Oil recovery was seeded. The two-peer run passed, but the three-peer gap remains unresolved and is not established as pre-existing.

[The first developer-options attempt](developer-options-first-failure.log) and [diagnostic retry](developer-options-diagnostic-failure.log) timed out despite successful real UDP admission: the fixture assumed two roster entries and overlooked two built-in practice cars. Correcting that fixture assumption allowed admission; the [unrestricted suite](developer-options.log) then failed on an unrelated aiming-limit mutation. The separate Oil-only mode passed all affected controls. The historical Round 3 stage-6 admission timeout is described in the Story report; no animation failure or production networking fix is inferred from it.

Human acceptance, manual keyboard/controller feel, exhaustive hose/mesh clearance, all tuning extremes, a driven ramp crest, authenticated EOS/WAN/multi-device operation, release-export performance and long-session soak remain unverified.

## Formal Astra review

[Story Round 4 critique](astra-round-4.md): **7.8 / 10 — FAIL (<8.0)**. The independent fresh rendered presentation/replacement run passed; [reviewer log](astra-native-runtime.log), [firing](astra-native-firing.png), [airborne](astra-native-airborne.png), [closer firing](astra-native-close-firing.png) and [impact](astra-native-impact.png) retain its direct evidence. Astra inspected all four concepts, 64 retained chronological frames and 32 fresh native frames. The mechanical presentation improved, but the smooth solid-looking discharge, angular splash wedges, synthetic deposit highlights and unresolved three-peer recovery keep the result below the quality gate. The short fresh run did not repeat expiry or sustained use; those findings rely on the primary full-run evidence. No further implementation followed the critique.
