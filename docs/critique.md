# Astra Runtime Critique and Human-Gated Quality Review

This document owns Astra's quality evaluation of the implemented outcome through runtime execution, playtesting, observation, and other operational evidence. Use `docs/standards.md` for Trackstorm architecture, testing, and code-quality requirements and `docs/workflow.md` for Jira scope, verification, branch, Pull Request, and independent engineering-review policy.

## Responsibility boundary

Astra critique answers:

> Does the implemented feature actually work well when exercised, played, seen, heard, or operated?

It does **not** duplicate the independent Pull Request review of source-code quality, architecture, maintainability, dependency direction, test design, documentation quality, versioning, CI, or delivery decisions. Those are reviewed independently after push as defined by `docs/workflow.md`.

This boundary preserves the part of Astra critique that static review cannot replace: actually running Trackstorm, interacting with the feature, observing player-facing behavior, and judging feel and polish.

## Applicability

- Every completed Jira Story receives a Story critique after all required children are implemented and verified on the Story branch.
- A meaningful Task/Subtask explicitly assigned on its own receives an individual Task critique after its scoped work is verified on the parent Story branch.
- A child completed during an already-authorized full Story assignment is an internal checkpoint, not a mandatory human-gated critique. Verify it and continue to the next required child.
- Technical or tooling Stories still receive critique. Exercise their observable or operational outcome rather than substituting a static code review.

A Story critique evaluates the integrated Story rather than merely aggregating child results. It must exercise the complete feature and relevant integration behavior where technically possible.

## Required sequence

```text
Implement
    ↓
Targeted iteration verification and runtime observation
    ↓
Fix clear in-scope defects discovered during implementation
    ↓
Comprehensive final Story verification
    ↓
Runtime / experiential critique
    ↓
Score
    ↓
Recommendation(s) if FAIL
    ↓
STOP
    ↓
Human decision
```

Verification must precede the formal critique. During implementation and verification, clear in-scope defects exposed by testing or playtesting are normal implementation work: correct them and re-test before the formal critique. Do not defer an obvious broken layout, unstable vehicle, incorrect transition, runtime exception, or similarly unambiguous requirement defect merely to manufacture a critique finding.

The formal critique evaluates the completed result. It must not become a second source-code review.

## Evidence requirements

Base critique findings primarily on direct runtime or operational evidence. Depending on the Story, launch and exercise Trackstorm; use one and multiple vehicles; test collisions; navigate affected UI; exercise normal, boundary, failure, rapid/repeated, reconnect, multiplayer or state-transition behavior; observe physics, camera, visual, audio and performance behavior; or execute the developer/tooling workflow being changed.

Runtime behavior must be tested when relevant and technically possible. Multi-entity, multi-car, multiplayer, reconnect, failure, persistence, repeated-use, or sustained-runtime scenarios remain required whenever they are material to the feature. These scenarios belong to verification even when they also inform critique.

For technical features without a player-facing presentation, critique the actual operational result where possible. Examples include running the affected tool, exercising a CI/workflow path, verifying displayed/compatibility version behavior, or observing serialization/recovery behavior. If the outcome cannot be executed meaningfully in the current environment, state that limitation; do not replace missing runtime evidence with a code-quality score.

Never claim that something was run, played, viewed, heard, network-tested, stress-tested, or otherwise observed when it was not.

Label evidence when the distinction matters:

- **VERIFIED** — directly exercised or observed.
- **INFERRED** — a runtime/operational conclusion supported indirectly but not directly exercised.
- **UNVERIFIED** — could not be meaningfully exercised in the current environment.

Report verification limitations explicitly. Material uncertainty may reduce the score.

## Quality scale

Score the observed resulting feature from **0.0–10.0**:

| Score | Standard |
| --- | --- |
| 0–2 | **Broken:** fundamentally incomplete, unstable, unusable, or incorrect in operation. |
| 3–4 | **Poor:** partially functional but significant runtime, usability, gameplay, presentation, or integration problems remain. |
| ~5 | **Merely functional:** requirements may work, but the exercised result lacks enough robustness, usability, game feel, integration quality, or polish to be solid. |
| 6–7 | **Solid:** reliable, coherent, appropriately integrated, and suitable for the current milestone. **6.0 is passing.** |
| 8–9 | **Highly polished:** professional, intentional, refined, robust, and difficult to improve substantially without added scope. |
| 10 | **Exceptional:** meaningful in-scope experiential or operational improvements are extremely difficult to identify. This score is rare. |

The score advises the human; it does not authorize changes or remove the human gate. Never inflate, round, or manipulate a score to reach 6.0. Score the observed result, not effort, change volume, test count, or critique-round number.

## Runtime / experiential review categories

Score only categories materially relevant to the Story and only to the extent they can be evaluated through execution or observation.

- **Runtime Functionality:** required behavior, player/developer-visible failure paths, completeness and repeated use as exercised.
- **Gameplay / Fun:** clarity, enjoyment, satisfaction, pacing and suitability for Trackstorm.
- **Controls / Responsiveness:** predictable input, latency, feedback and intentional handling.
- **Vehicle / Movement Feel:** weight, momentum, acceleration, braking, steering, grip, sliding, collisions, recovery and speed perception.
- **Physics:** observed stability, clipping, tunneling, jitter, impulses, consistency and exploitable behavior.
- **Multiplayer / Networking Experience:** observed peer consistency, latency behavior, disconnect/reconnect, join/resume, prediction/reconciliation symptoms and multi-client outcomes.
- **Camera:** framing, smoothing, responsiveness, visibility, obstruction, orientation and speed perception.
- **Visual Quality:** models, materials, textures, lighting, scale, composition, readability, hierarchy and cohesion as rendered.
- **Animation / Motion:** transitions, timing, interpolation, procedural motion, impact and continuity.
- **VFX / Feedback:** whether important actions and events are clearly communicated.
- **Audio:** timing, impact, volume, layering, repetition, spatialization and gameplay usefulness.
- **UI / UX:** readability, hierarchy, screen usage, discoverability, focus/navigation, feedback, flow and friction in actual use.
- **Game Feel / Juice:** anticipation, impact and recovery, particles, motion, camera and audio response; more effects do not automatically mean better feel.
- **Observed Performance:** frame-time symptoms, stalls, obvious scaling degradation or resource symptoms visible during the exercised scenarios.
- **Runtime Stability:** crashes, exceptions, warnings, invalid runtime state, lifecycle failures and consistency as observed.
- **Runtime Integration:** interaction among the feature's components and with surrounding systems as exercised.
- **Feature-Specific Operational Quality:** any observable category unique to the Story that is not covered above.

Do **not** score Code Quality, Architecture, static Testing/Reliability, naming, maintainability, dependency direction, implementation elegance, documentation quality, version maintenance, or PR hygiene here. Those belong to the independent engineering review.

## Critique output

Use the appropriate heading:

```text
Astra Runtime Critique — Task Round X
```

or:

```text
Astra Runtime Critique — Story Round X
```

Always include:

- **Overall Score: X.X / 10**
- **Quality Assessment: FAIL (<6.0) / PASS (>=6.0)**
- **Category Scores:** only materially relevant runtime/experiential categories.
- **What Was Exercised:** concise evidence, including VERIFIED / INFERRED / UNVERIFIED labels where useful.
- **Runtime / Operational Limitations:** anything not directly exercised or observed.

For a **FAIL** below 6.0, include at least one meaningful, evidence-based recommendation. For each material problem state:

- **Problem:** what is wrong in the observed outcome.
- **Evidence:** what exposed it.
- **Severity:** Critical / High / Medium / Low.
- **Impact:** why it matters to the player, operator, or feature behavior.
- **Suggested Improvement:** a specific corrective action.
- **Scope:** In Scope / Out of Scope.
- For a Story, **Corrective Work Type:** Existing Task / New Corrective Task / No Code Change Required.

Also include **Recommended Next Round**, prioritizing what should change if the human authorizes another round. An out-of-scope finding must be labeled **Out-of-Scope Recommendation** and must not be implemented without approval.

For a **PASS** at or above 6.0, problems, suggestions, and a recommended next round are not required and should normally be omitted. Do not invent low-value criticism merely to populate those sections.

The threshold rule is exact:

```text
Score < 6.0  → FAIL; meaningful recommendation(s) required
Score >= 6.0 → PASS; recommendation not required
```

## Mandatory human gate

**After presenting any formal critique, STOP and wait for explicit human instruction.**

Do not implement critique recommendations, begin another critique round, create corrective Jira issues, make unrelated changes, or proceed merely because an improvement appears desirable. This gate applies to the formal critique; it does not prohibit fixing clear in-scope defects discovered earlier during ordinary implementation and verification.

The human may accept the work as-is, select or reject recommendations, request clarification, authorize specific corrective work, request another critique round, defer an issue, or reject/redesign the implementation. A passing score does not prevent additional human-requested work, and a failing score does not prevent the human from accepting the result for the milestone.

PR timing and the Story-only delivery model remain governed by `docs/workflow.md`.

## Human-authorized improvement rounds

Each improvement round requires separate, explicit human authorization. Authorization for Round 2 does not authorize Round 3.

After authorization:

```text
Implement only approved corrections
        ↓
Re-run applicable verification
        ↓
Exercise the corrected runtime/operational result
        ↓
Critique independently
        ↓
New score + recommendation(s) if FAIL
        ↓
STOP for the next human decision
```

Story corrections remain on the existing Story branch. A corrective Jira Task/Subtask may be useful for traceability, but it never receives a branch or PR. Follow `docs/workflow.md` for scope and delivery rules.

An explicitly assigned individual Task/Subtask may have at most **3 Task critique rounds**. A Story may independently have at most **3 Story critique rounds**. No fourth round exists at either level.

## Review integrity

Judge what was actually exercised. Do not search for reasons to pass, reward implementation effort, or infer runtime quality from clean code or passing automated tests. The purpose of this critique is to provide evidence and judgment that an independent static PR review cannot reproduce.
