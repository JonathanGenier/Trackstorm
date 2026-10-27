# TS-245 Round 2 runtime evidence

These are direct Godot 4.7.2 .NET Compatibility renderer captures (NVIDIA GTX 1070), not concept renders or human playtesting. The host viewport is 1280×720. Other local UDP peers run production physics, replication and presentation nodes with their viewport rendering disabled.

[Formal Astra Round 2 critique](astra-round-2.md): **7.5 / 10 — FAIL (<8.0)**. It includes a fresh rendered presentation run and direct inspection of these retained images and temporal frames. Remaining concerns are the ribbon-like stream and flat band-like deposits. Human review is pending; no subsequent polish was performed.

## Visual observation

- [Closed box on the rising rack](closed-box.png) and [deployed jester](deployed.png) are unretouched frames 324 and 420 from the final native run. They show the corrected crimson, ivory, black and brass palette. The position buffers in the GLB are unchanged from the first-round asset; see [asset validation](asset-validation.json).
- [Moving straight, firing](stage-1-firing.png), [turning, firing](stage-2-firing.png), [airborne firing](stage-3-firing.png) and [replacement activation](stage-5-firing.png) show the connected, substantial black stream.
- [Straight deposits](straight-deposits.png), [curved deposits](curved-deposits.png), [airborne deposits](airborne-deposits.png) and [wall deposits](wall-deposits.png) frame the retained oil itself. The dark paired tire marks beside the curved oil are ordinary vehicle skid marks. The vertical wall is in shade. The bank uses the repository's existing Asphalt material and collision identity.
- [Replacement ready](replacement-ready.png) shows the Wrench after Oil retracts. [Repeated use](repeated-use.png) belongs to the twelve-activation sequence.

Animated recordings: [deployment and straight firing](deployment-straight.webp), [turning](turning.webp), [airborne](airborne.webp), [wall](wall.webp), [replacement](replacement.webp). These encode the original PNG sequence at its **simulation-frame spacing** (60 physics ticks/second), not measured wall-clock frame rate. No image appearance was retouched. Readback/PNG encoding substantially slowed the capture stages. Camera changes are fixture observation cuts, not production chase-camera behavior.

The older automatically named `closed.png` catches the lid opening; `ready.png` catches firing onset because saving waits for a rendered frame. Use the selected `closed-box.png` and `deployed.png` for those states.

## Functional and performance evidence

[Native jack log](jack-runtime.log), [scenario assertions](jack-evidence.txt), [trace](jack-trace.json) and [summary](summary.json) record four UDP peers, 30 ms delay/5 ms jitter/2% loss, a supported 23-degree ramp query, native bank driving, airborne impacts, replacement, deadline expiry and twelve repeated activations. The ramp query tests actual native impact support/orientation; it is not a driven ramp-crest test.

The final run recorded activation tick **1820**, deadline **3620**, and removal at **3620**. Accepted peer publications past that deadline contain no copy of the hazard. Final cleanup asserts zero Oil visual owners on all four peers.

During repeated use without motion capture, sampled render intervals were median **16.666 ms**, p95 **33.332 ms**, maximum **43.037 ms**, with at most six active hazards. Final cleanup samples had median/p95 **16.666 ms**. Peak head displacement was **0.410 m**, below the 0.45 m bound. These are sampled multi-arena fixture observations with other local applications present, not a controlled benchmark or a 60 FPS guarantee. The earlier capture stages had median **43.854 ms** and p95 **140.557 ms** including image readback/encoding.

[Contact log](contacts.log) records two owner and three rival entries on one surviving hazard, no repeated-overlap awards, agreed 150 rival-contact points, and four-peer admission retaining 1,500 exact timer-only hazards. [Handling](handling.log), [reconnect](reconnect.log), [rack](rack.log), [migration](migration.log), [repository gate](check.log) and [startup](startup.log) retain the corresponding command output; the Story report gives their final status and any repaired failures.

Human acceptance, keyboard/controller feel, competition balance, a driven ramp crest, authenticated EOS/WAN/multi-device operation, release-export performance and long-session soak remain unverified.
