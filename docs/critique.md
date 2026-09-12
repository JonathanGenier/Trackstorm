# Astra Self-Critique and Human-Gated Quality Review

This document owns quality evaluation, scoring, critique output, and human-authorized improvement rounds. Use `docs/standards.md` for Trackstorm architecture, testing, and code-quality requirements and `docs/workflow.md` for Jira scope, verification, branch, and Pull Request policy.

## Applicability

- Every completed Jira Story receives a Story critique after all required children are implemented and verified on the Story branch.
- A meaningful Task/Subtask explicitly assigned on its own receives an individual Task critique after its scoped work is verified on the parent Story branch.
- A child completed during an already-authorized full Story assignment is an internal checkpoint, not a mandatory human-gated critique. Verify it and continue to the next required child.

A Story critique evaluates the integrated Story rather than merely aggregating child results. It must examine whether the children work together as one coherent feature and whether the complete Story and child acceptance criteria are satisfied.

## Required Sequence

```text
Implement
    ↓
Verify
    ↓
Critique
    ↓
Score
    ↓
Recommendation(s) if FAIL
    ↓
STOP
    ↓
Human decision
```

Verification must precede critique. Complete the applicable checks in `docs/workflow.md`, then review the implementation as though another professional game-development team submitted it. Search for real weaknesses; do not defend prior decisions or treat compilation, passing tests, effort, or acceptance-criteria compliance alone as proof of high quality.

## Evidence Requirements

Base findings on direct evidence wherever possible. Depending on the work, build and test the project; inspect the complete diff; launch and exercise Trackstorm; inspect warnings; test normal, boundary, failure, rapid/repeated, and integration behavior; and observe relevant gameplay, physics, networking, UI, visual, audio, or performance behavior.

Runtime behavior must be tested when relevant and technically possible. Never claim that something was run, played, viewed, heard, network-tested, or otherwise observed when it was not.

Label evidence when the distinction matters:

- **VERIFIED** — directly tested or observed.
- **INFERRED** — concluded from code or design inspection but not directly exercised.
- **UNVERIFIED** — could not be meaningfully tested in the current environment.

Report verification limitations explicitly. An unverified area may reduce the score when it creates material uncertainty.

## Quality Scale

Score the resulting implementation from **0.0–10.0**:

| Score | Standard |
| --- | --- |
| 0–2 | **Broken:** fundamentally incomplete, unstable, unusable, or incorrect; examples include build failures, crashes, major omissions, or severe regressions. |
| 3–4 | **Poor:** partially functional but significant problems remain. |
| ~5 | **Merely functional:** may compile, pass tests, and meet acceptance criteria but lacks enough robustness, integration quality, usability, game feel, or polish to be solid. |
| 6–7 | **Solid:** reliable, coherent, tested, appropriately integrated, and suitable for the current milestone. **6.0 is passing.** |
| 8–9 | **Highly polished:** professional, intentional, refined, robust, and difficult to improve substantially without added scope. |
| 10 | **Exceptional:** meaningful in-scope improvements are extremely difficult to identify. This score is rare. |

The score advises the human; it does not authorize changes or remove the human gate. Never inflate, round, or manipulate a score to reach 6.0. Score the current result, not effort, change volume, test count, or critique-round number. A later round may score lower if it introduces regressions or reveals previously missed problems.

## Review Categories

Score only categories materially relevant to the work. Evaluate each against Jira requirements, `docs/standards.md`, current feature intent, and observed behavior.

- **Functionality:** required behavior, acceptance criteria, validation, failure paths, and completeness.
- **Gameplay / Fun:** clarity, enjoyment, satisfaction, pacing, and suitability for Trackstorm.
- **Controls / Responsiveness:** predictable input, latency, feedback, and intentional handling.
- **Vehicle / Movement Feel:** weight, momentum, acceleration, braking, steering, grip, sliding, collisions, recovery, and speed perception.
- **Physics:** stability, clipping, tunneling, jitter, impulses, consistency, and exploitable behavior.
- **Networking:** authority, synchronization, prediction, reconciliation, interpolation, latency, packet order, disconnects, and peer consistency.
- **Camera:** framing, smoothing, responsiveness, visibility, obstruction, orientation, and speed perception.
- **Visual Quality:** models, materials, textures, lighting, scale, composition, readability, hierarchy, and cohesion.
- **Animation / Motion:** transitions, timing, interpolation, procedural motion, impact, and continuity.
- **VFX / Feedback:** whether important actions and events are clearly communicated.
- **Audio:** timing, impact, volume, layering, repetition, spatialization, and gameplay usefulness.
- **UI / UX:** readability, hierarchy, screen usage, discoverability, feedback, flow, and friction.
- **Game Feel / Juice:** anticipation, impact and recovery, particles, motion, camera and audio response; more effects do not automatically mean better feel.
- **Performance:** frame time, allocations, processing, physics cost, resource use, networking scale, and obvious scalability risks.
- **Stability:** crashes, exceptions, warnings, invalid state, lifecycle, initialization, cleanup, and consistency.
- **Code Quality:** readability, maintainability, naming, complexity, duplication, coupling, unnecessary abstraction, and consistency.
- **Testing / Reliability:** regression protection, edge cases, boundaries, failure behavior, state transitions, invariants, and determinism.
- **Architecture:** compliance with the ownership and dependency requirements in `docs/standards.md`.
- **Integration:** interaction between components and, especially for Stories, between all child implementations.

## Critique Output

Use the appropriate heading:

```text
Astra Self-Critique — Task Round X
```

or:

```text
Astra Self-Critique — Story Round X
```

Always include:

- **Overall Score: X.X / 10**
- **Quality Assessment: FAIL (<6.0) / PASS (>=6.0)**
- **Category Scores:** only materially relevant categories.
- **What Was Verified:** concise evidence, including VERIFIED / INFERRED / UNVERIFIED labels where useful.
- **Verification Limitations:** anything not directly tested or observed.

For a **FAIL** below 6.0, include at least one meaningful, evidence-based recommendation. For each material problem state:

- **Problem:** what is wrong.
- **Evidence:** what exposed it.
- **Severity:** Critical / High / Medium / Low.
- **Impact:** why it matters technically or to the player.
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

## Mandatory Human Gate

**After presenting any critique, STOP and wait for explicit human instruction.**

Do not implement recommendations, begin another critique round, create corrective Jira issues, make unrelated changes, or proceed merely because a fix appears obvious. This rule applies regardless of score, test status, severity, or whether the human is likely to approve the recommendation.

The human may accept the work as-is, select or reject recommendations, request clarification, authorize specific corrective work, request another critique round, defer an issue, or reject/redesign the implementation. A passing score does not prevent additional human-requested work, and a failing score does not prevent the human from accepting the result for the milestone.

PR timing and the Story-only delivery model remain governed by `docs/workflow.md`.

## Human-Authorized Improvement Rounds

Each improvement round requires separate, explicit human authorization. Authorization for Round 2 does not authorize Round 3.

After authorization:

```text
Implement only approved corrections
        ↓
Re-run applicable verification
        ↓
Critique the current result independently
        ↓
New score + recommendation(s) if FAIL
        ↓
STOP for the next human decision
```

Story corrections remain on the existing Story branch. A corrective Jira Task/Subtask may be useful for traceability, but it never receives a branch or PR. Follow `docs/workflow.md` for scope and delivery rules.

An explicitly assigned individual Task/Subtask may have at most **3 Task critique rounds**. A Story may independently have at most **3 Story critique rounds**. No fourth round exists at either level.

## Review Integrity

Treat your own work as another developer's submission. Do not search for reasons to pass, reward effort, or assume a later round deserves a higher score. The objective is an accurate, evidence-based assessment that lets the human decide whether further quality work is worth its scope and cost.
