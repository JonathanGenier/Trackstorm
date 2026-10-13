# TS-245 Round 7 runtime evidence

Actual Godot 4.7.2 Compatibility captures on a GTX 1070, 1280×720. Native scripted driving and local UDP checks are automated evidence, not human playtesting. The [Story report](../../ts-245.md) records outcomes and unresolved limits.

Retained text uses repository LF line endings; incidental trailing log whitespace and empty end lines are normalized without changing diagnostic content. Baseline-control patches use zero context to preserve exact changes without whitespace-only context lines. Original execution outputs remain in the local ignored verification workspace.

## Painted tank and liquid

- [Production chase-camera lettering and meter](gameplay-meter-full.png), [gameplay firing](gameplay-stage-1-firing.png), [gameplay sequence](gameplay-driving.webp).
- [Close full meter and curved tank paint](meter-full.png), [draining](meter-half.png), [empty](meter-empty.png), [tank in the assembly](tank-detail.png).
- [Straight burst / braking / bump](deployment-straight.webp), [chronological sheet](deployment-straight-sequence.png), [finer firing samples](firing-1-sequence.png).
- [Airborne burst](airborne.webp), [chronological sheet](airborne-sequence.png), [finer airborne samples](firing-3-sequence.png).
- [Turning samples](firing-2-sequence.png), [impact skirt](impact.png), [mouth closure](mouth-closing.png), [retraction and replacement](replacement.webp).
- [Curved deposits](curved-deposits.png), [straight deposits](straight-deposits.png), [airborne deposits](airborne-deposits.png), [wall deposits](wall-deposits.png), [expiry fade](expired-fading.png), [repeated use](repeated-use.png).

Animated WebP spacing uses captured **60 Hz simulation frame numbers**, not measured wall-clock video. Contact sheets resize and arrange genuine frames. [Recording sources/hashes](recordings.json) and [firing sources/hashes](firing-sequences.json) identify the inputs. Earlier iteration files were excluded by capture time when retaining the final sequences.

## Clearance, deposition and bounded use

- [780-pose clearance evidence](clearance-evidence.txt), [log](clearance-runtime.log), [backward lean](clearance-clearance-0.24-20.png), [maximum width](clearance-clearance-0.80-20.png).
- [Grass-to-bank support and hard-drift evidence](bank-evidence.txt), [log](bank-runtime.log), [trace](bank-trace.json).
- Three bank firing timings: [first](bank-0.webp), [second](bank-1.webp), [airborne approach](bank-2.webp); resulting [first](bank-bank-0-deposits.png), [second](bank-bank-1-deposits.png), [airborne](bank-bank-2-deposits.png) deposits. Gray tire marks are separate from black Oil.
- [Full four-peer evidence](evidence.txt), [runtime log](runtime.log), [trace](trace.json), [repeated-use measurements](runtime-summary.json).
- [Contacts/scoring](contacts-final.log), [twenty handling trials](handling-final.log), [rack replacement with shutdown warning](rack-final.log), [unchanged-source verbose rack retry](rack-verbose-retry.log), [retry outcome](rack-retry-status.json), [Oil developer options](developer-options-final.log), [startup](startup-final.log).
- [Repository gate](check-final.log), [asset/anchor/hash validation](asset-validation.json), [Blender build](blender-final.log), [native import](import-final.log), [native result manifest](native-status.json).

## Recovery investigation

- [Initial three-peer failure](migration-before.log), [failing diagnostic](migration-diagnostic.log), [later passing diagnostic](migration-diagnostic2.log).
- [Generic reconnect diagnostic](reconnect-diagnostic.log), [final generic reconnect](reconnect-final.log), [final short three-peer migration](migration-final.log), [final two-peer migration](migration-two-peer.log).
- [First focused Oil reconnect](oil-reconnect-first.log), [final focused Oil reconnect](oil-reconnect-final.log): observer during firing, displayed meter, firing-owner interruption, exact forty-parcel continuation, original deadlines and cleanup.
- [Short Oil migration](oil-migration-first.log) passed restoration/continuation but did not establish eventual cleanup. [First extended failure](oil-migration-expiry.log), [per-peer cleanup diagnostic](oil-migration-cleanup-diagnostic.log), [final extended run](oil-migration-final.log) retain the stronger assertion and its actual outcome.
- Archived current-main control: [reconnect](main-reconnect.log), [short migration](main-migration.log), [sustained peer-continuation failure](main-continuation.log). [Source provenance](main-control-provenance.json), [exact added continuation assertion](MigrationIntegrationChecks-control.patch), [diagnostic-only Salvo change](SalvoRecoveryFixture-control.patch).

The extended control reproduces the stranded post-migration survivor without TS-245 production code. It does not establish a cause for every historical pre-agreement failure. No general networking fix, EOS/WAN coverage or all-green recovery result is claimed.

## Formal critique

**[Astra Runtime Critique — Story Round 7](astra-round-7.md): 7.9 / 10, FAIL.** Fresh native presentation passed; remaining findings are pointed detached-liquid slivers and the shared migration continuation failure. The critique separates fresh observation, retained outcomes and unverified behavior.

[Independent runtime log](astra-native-runtime.log), [measurements](astra-native-evidence.txt), [trace](astra-native-trace.json), [source hashes](astra-native-sources.json), [painted tank](astra-native-tank-detail.png), [airborne firing](astra-native-stage-3-firing.png), [chronological airborne samples](astra-sequence-airborne.png), [replacement sequence](astra-sequence-replacement.png). These distinct files preserve the full verification run above.
