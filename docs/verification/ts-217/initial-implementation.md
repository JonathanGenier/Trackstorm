> Superseded interaction/default assumptions. This records the earlier first-use implementation only; see the current TS-217 report for the approved selection-driven correction.

# TS-217 — Shield Rear Shield Deployment verification

Date: 2026-10-01. Branch: `ts-217-jg`. Jira TS-217 was read in full, including its empty child/comment lists and links. Completed dependency TS-216 and linked TS-218/TS-220 were read. Implementation began at current `origin/main` `aa1cc4c` (merged TS-216); main was fetched again before integrated verification with no divergence. Story version is `0.2.13` / export `0.2.13.0`.

## Implemented

- First ordinary use stages `Held -> RearShield` in the atomic item/world transaction. The physical slot, grant capability, persistent identity, damaged HP and replay watermark survive unchanged. Further uses are harmless; world-wall input is not implemented.
- Core-owned oriented-box geometry intercepts Machine Gun rays, Missile/Salvo segments and radial target paths, Proxy Mine contact paths, and native contact points. Covered hits spend only shield HP; the lethal hit remains blocked and following hits reach the car. Existing weapon amounts, radial falloff and vehicle collision tuning are unchanged.
- Collision observations collapse to one strongest shield impact per step. A physically intercepted vehicle-pair impact cannot also damage its owner's chassis through another manifold point. Per-pool collision cooldown and ordered damage survive item protocol 17 and nested checkpoints. The TS-216 damage evaluator remains shared by public host damage and staged hits.
- Native network bodies reconstruct a temporary plate collider and matching interpolated visual. Rack presentation relinquishes the deployed payload without releasing inventory. Lifecycle/accepted state controls removal and restoration. No new client hit-authority channel or shield pose stream exists.
- Added deterministic rear-shield tests and a two-peer native harness. Extended existing transport, rack, late-admission and reconnect fixtures. The optional reconnect variant uses the shield in place of the Machine Gun fixture to respect the existing two-slot capacity. Oil recovery now handles either physical slot.
- Updated affected item, vehicle, networking and recovery documentation and fast-check routing. No dependencies or third-party assets were added.

## Commands and deterministic evidence

- `git fetch origin main`; `tools/sync-story-version.ps1`; `tools/check-version.ps1`: PASS, main synchronized, expected version `0.2.13`.
- Targeted Core Shield/registry tests: 20 PASS. New `RearShieldTests`: 15 PASS, including both physical slots, HP/ID retention, duplicate use/damage, spatial rear/edge/front/side shots, rotated geometry, lethal blocking, missile impact uniqueness, rejected-batch rollback, stationary-defender collision routing, duplicate chassis/manifold suppression and codec/cooldown recovery.
- `tools/check-fast.ps1 -Area Core`: 956 PASS at that iteration; subsequent added two-slot test brings final Core total to 957.
- `tools/check-fast.ps1 -Area Transport`: 412 PASS.
- `check.ps1`: PASS, 957 Core tests and 412 non-native transport tests, Debug production and Release full-solution builds with warnings as errors. Version, workflow helper and media verification included. The automatic fast-check invocation reported no committed changes, so explicit areas were used; that no-op is not test evidence.
- Sandbox-only restore initially lacked access to user NuGet configuration; native Godot initially lacked its user log/certificate access. Successful runs used approved normal host access.

Evidence retained in [full gate log](check.log), [impaired shield run](rear-shield-impaired.log), [shield reconnect log](reconnect-shield.log), [moving shields](moving-shields.png), [destruction while driving](destroyed-while-driving.png), and [rear collision](rear-collision.png).

## Runtime / native evidence

Godot `4.7.2.stable.mono.official.ed1daf0bf`, Windows, local real UDP. Rendered verification used OpenGL/NVIDIA RTX 4070 Ti. No authenticated EOS or separate-PC claim is made.

- `check-rear-shield.ps1 -NoBuild`: PASS, eight scenarios through production native bodies and production drivers.
- `check-rear-shield.ps1 -Impaired -Visual`: PASS, 30 ms outbound delay, 5 ms jitter, 2% loss, same eight scenarios. Two independent shields deployed through ordinary local/remote input. Captures were opened and inspected: plates occupy the rear, follow driving, and remain while another item fires.
- Stationary rear, moving rear and inner rear-quarter fire: shield 900 -> 810 HP; protected vehicle remains 1000 HP. Outer rear-quarter: shield remains 900, vehicle 1000 -> 910. Side: vehicle 910 -> 820; front: 820 -> 730, with shield unchanged.
- Destruction during driving/sustained fire: shield 10 -> 0; subsequent rounds reduce vehicle 730 -> 651.25. Native rear collision: shield 900 -> 823.21387, vehicle remains 651.25. The other vehicle's shield remains 900 in every scenario. Replicated pools and collider presence converge after each scenario.
- `check-shield.ps1 -NoBuild -Impaired`: PASS, three real UDP peers, four independent pools, damaged rear-shield late admission, full authority/checkpoint restoration, stale damage rejection and exactly-once destruction.
- `check-reconnect.ps1` (default Machine Gun/Salvo fixture): PASS. `check-reconnect.ps1 -Shield`: PASS, three actual authenticated-test-identity resume cycles including 125 seconds offline. Damaged `WorldWall/RearShield` pools remain 700/600 HP with exact attachment/pose/watermark; native bodies are reused and prediction/interpolation reset. Production EOS identity is replaced by the established fixture identity.
- `check-gdunit.ps1`: PASS, plugin import and one Client test case, zero errors/failures/orphans. Main-scene headless startup (`--quit-after 120`): exit 0, no diagnostics.
- `check-car-rack.ps1`: PASS, full item catalog including Shield rear relocation and the consecutive-mine regression. `check-migration.ps1 -NoBuild`: PASS, three-peer authority succession and existing damaged wall recovery.
- Existing native item, Machine Gun, Salvo and Proxy Mine regression harnesses: PASS. These exercise the surrounding weapon paths without new damage balance.
- Additional routed native regressions PASS: `check-environment-collisions.ps1`, `check-landing.ps1`, `check-match.ps1`, `check-network-vehicles.ps1`, `check-nitro.ps1`, `check-oil.ps1`, `check-pickup-drive.ps1` (180/180 motion crossings), `check-terrain-handling.ps1`, `check-vehicle.ps1` (30/144 FPS replay), and `check-environment-collision-network.ps1` (30 ms delay / 5 ms jitter / 2% loss). These exercise surrounding systems and do not substitute for the shield-specific scenarios above.
- Iteration findings: the initial reconnect fixture assumed Oil used slot 0, then attempted three retained item types in two slots; corrected fixture selection and added the explicit Shield variant. The old rack assertion expected every retained item to remain on the rack; its Shield assertion now requires rear armor and a stowed rack. Those failed iteration runs are not PASS evidence.

## Assumptions, limits and explicitly unverified areas

- The plate is temporary TS-217 presentation: vehicle-width, 1.5 m high, 0.35 m thick at the documented fixed rear offset. Final art/animation/VFX, world-wall placement and balance remain outside scope. Spawn weight remains zero; host grants or configured weights acquire it.
- Two shields in one vehicle's slots use the same temporary mount. Stable identity ordering selects one pool per impact, without duplicate damage. Distinct vehicles retain independent pools.
- Protection tests actual finite geometry; it is not universal blast occlusion or directional immunity. Environmental damage such as water/out-of-bounds is unaffected. Unrelated collisions retain normal damage.
- Native scenarios use controlled fixture poses/inputs and a fixed observation camera; they do not establish physical-controller ergonomics, a human play session, exhaustive steep-bank/rollover contacts, long-duration performance, Internet/EOS conditions, cross-platform physics or independent-device behavior.
- Missile interception is directly covered by Core and existing native missile regression. Shield-specific Salvo/mine combinations have not been separately observed in native gameplay. Existing migration exercises damaged wall pools; full authoritative restoration with a rear shield is covered separately, and actual reconnect with the rear shield is directly exercised.
- Commit/push and independent post-push engineering review remain pending the repository's formal-critique human gate. No merge was performed.

## Astra runtime critique

Independent [Astra Story Round 1 critique](astra-round-1.md): **8.2/10 — PASS** against the exact 8.0 threshold. Astra independently reran the rendered impaired shield scenarios, lifecycle/late admission, and three actual reconnect cycles including 125 seconds offline, and inspected captured runtime views. No material in-scope runtime defect was observed; no corrective recommendations were proposed.

Category scores: runtime functionality 8.5, physics 8.0, multiplayer/networking experience 8.2, stability 8.5, integration 8.3, and feature-specific operational quality 8.0. The overall assessment is holistic. Controlled local scenarios and sampled still images do not establish human driving feel, continuous motion quality, Internet/EOS behavior, uneven-terrain coverage or every native weapon/shield combination. The complete critique records evidence and limitations. Delivery remains stopped for explicit human acceptance under `docs/critique.md`.

## Subsequent main synchronization

At the user's explicit request after the Round 1 critique, fetched and fast-forwarded `ts-217-jg` to current `origin/main` `27d274e` (35 commits after the original baseline). Preserved all uncommitted TS-217 work and reconciled mechanical version/documentation conflicts, retaining both main's handling documentation and shield documentation. Production code merged automatically; no new shield behavior was introduced. Story version recalculated to `0.2.18` / export `0.2.18.0`.

Reverification: [full gate](main-sync-check.log) PASS with 1,059 Core tests, 412 transport tests and warnings-as-errors builds; version/ancestry checks and `git diff --check` PASS. [Impaired native shield run](main-sync-shield.log) PASS for all eight scenarios with the same damage outcomes as the original run. The earlier Astra score remains historical evidence for its reviewed baseline; no new formal critique was performed. Changes remain uncommitted/unpushed pending the existing human acceptance gate. A safety stash retains the pre-synchronization work.
