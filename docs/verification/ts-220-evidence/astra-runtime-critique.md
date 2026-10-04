# Astra Runtime Critique — Story Round 1

TS-220 — Shield Final Integration, Rename & Balance. Independently exercised on 2026-10-04, branch `ts-220-jg`, integrated main `aecca15`, version `0.2.28`. This is the first formal critique; the earlier execution was paused for main integration before any score was issued. No implementation, tests, commits or delivery actions were changed by this reviewer.

**Overall Score: 8.2 / 10**

**Quality Assessment: PASS (>=8.0)**

The exercised Shield is coherent and polished across acquisition, repeated selection, rear protection, release into independent cover, damage and removal. Its folding carriage, distinctive painted armor, damage feedback and persistent per-slot durability make the same object legible through its lifecycle. Fresh local multiplayer scenarios retained exact ownership and damaged state through interference, admission and recovery. This score applies to the observed Shield integration, with a meaningful confidence reduction for unresolved broader native replication failures and the limited local fixture environment. It is not a declaration that the repository-wide native suite is green or that competitive balance is established.

## Category Scores

| Runtime category | Score | Observed basis |
| --- | --- | --- |
| Runtime Functionality | 8.8 | Independent pools, exact slot clearing, blocked-use retry, repeated use/discard, destruction and expiry behaved consistently. |
| Controls / Responsiveness | 8.3 | Normal selection/use inputs, rapid reversal, early-use queue/cancellation and repeated taps produced intentional outcomes under the exercised impairment. Physical ergonomics were not evaluated. |
| Physics | 8.3 | Walls slide, rotate and settle; vehicle contact slows the car; hard impacts topple and remove the wall without launching the car in the measured cases. |
| Multiplayer / Networking Experience | 7.6 | Fresh Shield-specific impaired peers, late join, reconnect and migration converged. Separate unresolved native freshness failures materially limit broader confidence. |
| Visual Quality | 8.5 | Clear rear/wall silhouette, consistent crimson/ivory panels, visible hinges and mechanical attachment; distinctive shape remains recognizable at stress-scene distance. |
| Animation / Motion | 8.4 | Folding, rack travel, intermediate release and breakup remain coherent through rapid selection and repeated deployment. Judged from fresh sampled rendered frames and traces, not continuous human play. |
| VFX / Feedback | 8.2 | Bright impact flash, persistent damage marks and separating destruction panels communicate interception, deterioration and removal. |
| UI / UX | 8.3 | Independent numeric HP and armor plates, selection border, low-HP color and empty-slot cleanup are legible in inspected native captures. |
| Runtime Stability / Integration | 8.0 | All fresh critique commands exited cleanly; long offline continuation, multiple walls and ordinary pickup integration worked. Broader recorded failures prevent an unqualified stability claim. |

Overall is an integrated judgment, not a test-count average. Gameplay enjoyment, audio, controller ergonomics and frame-time performance are not scored: the evidence does not support those judgments.

## What Was Exercised

**VERIFIED — fresh current-build execution.** Every command below ran after the completed post-integration verification, with the existing Debug build and Godot 4.7.2 Mono. All nine commands exited **0** and emitted their expected success result. Scripts were run individually, without competing native critique scenarios.

`$g` was `.godot/godot-ci/4.7.2/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`, resolved to its absolute path.

| Command | Fresh evidence | Direct result |
| --- | --- | --- |
| `./check-shield-presentation.ps1 -GodotPath $g -Impaired -Visual -NoBuild` | [Log](astra/presentation-post-hud.log) | Two native arenas: repeated selection/stow, duplicate identities, damage retention, normal deployment, remote deployment, destruction cleanup, reacquisition, queued early use/cancel/discard, driving and twenty-degree slope traversal. |
| `./check-rear-shield.ps1 -GodotPath $g -Impaired -Visual -NoBuild` | [Log](astra/rear-post-hud.log), [measurements](astra/rear-evidence-post-hud.txt) | Rear and rear-quarter blocked hits: Shield 900→810 HP, vehicle 1000→1000. Side/front shots spent vehicle HP while Shield stayed 900. Independent other pool stayed 900. Native rear collision: Shield 900→822.76117 while protected vehicle stayed 652.44385. Continued fire after Shield destruction subsequently reached the vehicle. |
| `./check-world-wall.ps1 -GodotPath $g -Visual -NoBuild` | [Log](astra/world-post-hud.log), [measurements](astra/world-evidence-post-hud.txt) | Blocked-use same-capability retry; flat/fast/reverse/banked deployment retains ID, 875 HP and exact slot; native pushing, weapons, third-peer admission, sixteen walls for 600 physics frames, toppling/ground break and captured expiry. |
| `./check-reconnect.ps1 -GodotPath $g -Shield -Visual -NoBuild` | [Log](astra/reconnect-post-hud.log) | Three arena resyncs including 125 seconds offline. One damaged attached pool retained, one expired wall absent; exact HP, expiry, tipping, pose and damage watermark, without refill or lifetime restart. |
| `./check-migration.ps1 -GodotPath $g -Players 2 -NoBuild` | [Log](astra/migration2-post-hud.log) | Native two-peer migration retains two installed damaged wall continuations and exact selected state. The two-player fixture seeds validated continuation during Countdown; it does not prove ordinary deployment during Countdown. |
| `./check-migration.ps1 -GodotPath $g -Players 3 -NoBuild` | [Log](astra/migration3-post-hud.log) | Native three-peer continuation retains two damaged wall pools, exact pose/HP/expiry/watermarks and sequential authority epochs. |
| `./check-shield.ps1 -GodotPath $g -Impaired -NoBuild` | [Log](astra/shield-post-hud.log) | Four independent pools, duplicate damage/repeated destruction rejection, exact slot/state restoration, three-peer admission; native HUD resources clear after ordinary discard and show fresh replacement HP without stale durability. |
| `./check-hud.ps1 -GodotPath $g -NoBuild` | [Log](astra/hud-post-hud.log) | Native rendered durability at full, damaged, low, fractional and destroyed HP; independent second pool, repeated selection, retired-slot/life clearing, reconstructed damaged HUD, nine sizes. |
| `& $g --path . res://scenes/verification/pickup_drive_checks.tscn -- "--pickup-output=$out" --pickup-impaired` | [Log](astra/pickup-inventory-post-hud.log) | Fresh rendered inventory phase on the production map: both application peers physically cross pickup radius and acquire every item, including Shield, fill two slots, reject full inventory and use/retry. `$out` was absolute `.godot/ts-220/astra-pickup-post-hud`. The separate motion-reliability phase was not run here. |

Presentation/rear impairment was 30 ms outbound delay, 5 ms jitter and 2% loss. These are real local UDP/native Godot arenas with scripted input and fixture setup, not remote-device or WAN sessions.

**VERIFIED — actual visual and trace inspection.** I opened the newly generated images with the image-view tool and inspected the rendered pixels, as well as current result logs and selected presentation state traces:

- [Mounted armor](astra/presentation-mounted-post-hud.png), [expanded wall](astra/presentation-expanded-post-hud.png), [breakup](astra/presentation-destruction-post-hud.png), and [chase driving](astra/presentation-mounted-driving-post-hud.png): one recognizable design through mounted and released states, distinct damage/removal, forward sightline retained above the large rear armor. The model intentionally hides much of the car from behind.
- [Rear interception](astra/rear-hit-post-hud.png): muzzle/ray contact and bright Shield impact response at the protected rear face, accompanied by the separate HP measurements above.
- [Sixteen simultaneous walls](astra/world-sixteen-walls-post-hud.png) and [post-topple cleanup](astra/world-toppled-removed-4-post-hud.png): distinct upright walls and removal after the hard-impact sequence. The underlying fresh log measured car 16.63→16.15 m/s on first contact, off-centre wall travel 5.83 m, three-second coast settling from 8 to 0.01 m/s, banked contact rise 0.13 m, and zero peak upward rise/speed in all five hard-hit cases.
- [675/900 HP](astra/hud-shield-675.0-post-hud.png), [20/900 HP](astra/hud-shield-20.0-post-hud.png), and [destroyed/900 HP](astra/hud-shield-0.0-post-hud.png): plates drain only for the damaged slot, low HP turns orange, destruction removes its icon/value/plates while preserving the other slot.
- [640×360](astra/hud-hud-640x360-post-hud.png) and [1920×1080](astra/hud-hud-1920x1080-post-hud.png): both retained values (675 and 123) and selected-slot outline remain distinguishable. Fine HP unit lettering is necessarily small at 640×360, but the numeric values and warning state remain visible.
- [Host pickup](astra/pickup-driver-0-post-hud.png) and [remote pickup](astra/pickup-driver-1-post-hud.png): actual production-map acquisition produces the Shield name/icon and mounting presentation on each peer. This fixture creates its own HUD without a Shield-state provider, so its screenshots omit durability. That is an evidence limitation of this fixture, not evidence that the production bootstrap loses HP: production bootstrap supplies the provider; the dedicated native HUD and three-peer state checks above exercise it. No fixture correction was implemented during critique.
- [Expanded state trace](astra/presentation-expanded-post-hud.json), [damaged trace](astra/presentation-wall-damage-post-hud.json), [cleanup trace](astra/presentation-cleanup-post-hud.json): preserved identity/HP through release, independently damaged walls and bounded destruction effect cleanup.

## Runtime / Operational Limitations and Unresolved Risks

**VERIFIED only as reviewed external run records, not newly executed by this reviewer:** the parent verification's [post-HUD native transport selection](native-transport-post-hud.log) and [exact-selection repeat](native-transport-repeat.log) each report 14 passes and one failure. The failing profiles differ, but both fail `clients.All(client => client.Latest?.Tick > staleTick)`. These remain unresolved. Successful Shield scenarios do not erase those failures; neither flakiness, harmlessness nor root cause is established.

The [Story report](../ts-220.md) also retains Mine knockback, Nitro rest-launch, pickup motion-reliability and Salvo marker failures, plus an initial terrain native-finalizer abort followed by a clean isolated repeat. I did not rerun those neighboring scenarios in this critique. **INFERRED:** their inheritance from current main is supported by earlier recorded results and source comparison, not an independently rebuilt current-main baseline. They prevent a claim of clean repository-wide native verification. Fresh inventory-phase success does not resolve the separate pickup motion failure.

**UNVERIFIED:** competitive balance, human free play, audio listening, physical controller ergonomics, WAN/EOS authentication/NAT, independent devices, exported-release acceptance, the ten-minute soak and long-session performance. Default HP/size/mass/lifetime behavior was exercised locally; no evidence warrants claiming final balance or changing the approved defaults. The native HUD fixture renders and reads back many captures, and its visible FPS labels vary substantially; these screenshots are not a controlled performance benchmark. No frame-rate score is assigned.

Fixture grants, trusted identities, initial damage and temporary poses isolate lifecycle/geometric cases. Ordinary input, actual native projectile/contact hits and a separate physical pickup drive provide complementary execution. This was not one uninterrupted human match. The reconnect screenshot shows retained standings, not a close-up durability comparison; persistence evidence comes from the exact native checkpoint/state comparisons. Native contact caches and cosmetic animation progress are not persisted, so recovery does not establish seamless continuation of those transient details.

The initial logs/captures in `astra/` without `post-hud` in their names belong to the earlier pre-main-integration execution (presentation run `check-20261004-052327`). They are preserved as actual historical observations and are not substituted for the fresh current-build results. Fresh presentation captures originate from `check-20261004-055120`; fresh HUD captures originate from `.godot/hud-checks/f95fa50a6ba3409a81d2823977c314a3`.

No in-scope Shield correction is recommended from this exercised result. The critique stops here; it does not authorize another round, production changes, Jira work or merge.
