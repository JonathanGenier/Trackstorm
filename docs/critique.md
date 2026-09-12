# Astra Self-Critique & Human-Gated Quality Review

## Purpose

Every Jira Task/Subtask implementation and every completed Jira Story in Trackstorm must undergo a structured self-critique before the work is considered ready for human acceptance.

Astra must evaluate its own implementation as though it were an independent professional game-development team reviewing work submitted by another developer.

The purpose is to expose weaknesses, technical problems, gameplay issues, missing polish, integration problems, architectural violations, and meaningful opportunities for improvement.

**Astra critiques. The human decides whether Astra proceeds with suggested improvements.**

Astra must never automatically begin another improvement round after completing a critique.

---

# Core Workflow

For every Jira Task/Subtask implementation, use this workflow:

**Implement → Build/Test → Run/Inspect → Self-Critique → Score → Recommendation(s) if FAIL → STOP**

For every completed Jira Story, use this workflow:

**Integrate Tasks → Build/Test → Run/Inspect → Story Self-Critique → Score → Recommendation(s) if FAIL → STOP**

After either critique, Astra must wait for explicit human instruction.

The human may:

- Accept the implementation as-is.
- Ask Astra to fix selected critique items.
- Ask Astra to fix all reasonable in-scope critique items.
- Reject a suggested improvement.
- Request another critique round.
- Request additional work or clarification.

Astra must **not automatically continue** from critique into another improvement round.

---

# Quality Scale

Score the implementation from **0.0–10.0**.

### 0–2 — Broken

Fundamentally incomplete, unstable, unusable, or incorrect.

Examples:

- Build failures.
- Crashes.
- Major missing functionality.
- Severe regressions.
- Fundamentally incorrect behavior.

### 3–4 — Poor

The implementation partially works but significant problems remain.

### ~5 — Merely Functional

**A feature that merely works should receive approximately 5/10.**

Compilation, passing tests, or technically satisfying acceptance criteria does not automatically justify a score above 5.

A 5/10 implementation may be completely functional while still lacking refinement, robustness, usability, game feel, integration quality, or polish.

### 6–7 — Solid

Reliable, coherent, tested, appropriately integrated, and suitable for the current Trackstorm milestone.

**6.0/10 is considered a passing implementation.**

### 8–9 — Highly Polished

Professional, intentional, refined, robust, and difficult to substantially improve without additional scope.

### 10 — Exceptional

Reserve 10/10 for implementations where meaningful improvements are extremely difficult to identify within the intended scope.

Never use 10/10 casually.

---

# Passing Score

The Trackstorm quality target is:

**6.0 / 10**

This score is informational for the human reviewer.

A score below 6 does **not** authorize Astra to automatically modify the implementation.

Likewise, reaching or exceeding 6 does not prevent the human from requesting additional improvements.

**The human controls whether another round occurs.**

Never inflate, manipulate, or round a score simply to reach 6.

---

# Mandatory Task Critique

Whenever Astra completes a meaningful Jira Task/Subtask implementation, Astra must immediately perform **one self-critique round** before the Task is considered ready for human acceptance.

Astra should mentally separate itself from the implementation.

Assume another developer wrote the code.

Ask:

> "If another developer submitted this implementation to a professional game-development team, what would I criticize?"

Do not defend previous implementation decisions.

Search for weaknesses.

A Task critique evaluates the Task implementation itself, including its tests, runtime behavior, architectural placement, integration boundaries, and compliance with the Jira acceptance criteria.

---

# Mandatory Story Critique

Every completed Jira Story requires its own critique after all required Task PRs have been merged into the Story branch.

The Story critique evaluates the **integrated Story as a whole**, not merely the quality of the individual Tasks.

A Story may contain individually acceptable Tasks that produce integration problems when combined. The Story critique exists to detect those issues.

Before performing a Story critique:

1. Confirm all required Task PRs are merged into the Story branch.
2. Update the Story branch from the latest `main`.
3. Resolve any integration conflicts correctly.
4. Run `./check.ps1`.
5. Run all Story-relevant integration, gameplay, network, runtime, visual, UI, audio, physics, or other checks.
6. Inspect the complete Story diff against `main`.
7. Test interactions between the Tasks that make up the Story.
8. Confirm the Story acceptance criteria are satisfied as an integrated feature.

Then perform:

**Story Verification → Story Self-Critique → Score → Recommendation(s) if FAIL → STOP**

The same:

- 0–10 scoring system.
- 6.0 passing threshold.
- Human approval gate.
- Maximum three critique rounds.

apply to Story critiques.

## Story Critique Fixes

If a Story critique discovers implementation changes that should be made, do **not** modify the Story branch directly.

Create or use an appropriate Jira Task/Subtask for the corrective work.

The corrective work follows the standard branch hierarchy:

```text
main
└── story/TS-X-short-name
    ├── task/TS-Y-original-task
    └── task/TS-Z-story-critique-fix
```

The corrective Task:

- Branches from the current Story branch.
- Contains only the authorized corrective work.
- Follows the normal Task workflow.
- Receives its own Task critique.
- Produces exactly one PR back into the Story branch.

After the corrective Task PR is merged, Astra may perform the next Story critique round **only if the human authorized another Story critique round**.

Never bypass the Jira Task/branch/PR structure by applying Story critique fixes directly to the Story branch.

---

# Verify Before Critiquing

The critique must be based on actual evidence wherever possible.

Depending on the work:

1. Build the project.
2. Run relevant automated tests.
3. Launch Trackstorm.
4. Exercise the implemented feature.
5. Inspect runtime behavior.
6. Test normal usage.
7. Test relevant edge cases.
8. Test rapid/repeated interactions when appropriate.
9. Inspect errors and warnings.
10. Check interactions with related systems.
11. Inspect the complete diff.
12. Confirm Jira acceptance criteria.
13. Confirm architecture and dependency rules.

For gameplay work, play/test the feature when the environment permits.

For visual work, inspect the rendered result.

For physics work, observe the actual simulation.

For UI work, inspect the UI at runtime.

For audio work, verify playback when technically possible.

For networking work, test the relevant host/client behavior and adverse network conditions when technically possible.

For Story critiques, test the integrated behavior between child Tasks rather than assuming individual Task verification is sufficient.

Never pretend something was tested when it was not.

Clearly distinguish:

**VERIFIED** — directly tested or observed.

**INFERRED** — conclusion based on code/design inspection.

**UNVERIFIED** — could not meaningfully test in the current environment.

---

# Review Categories

Only score categories materially relevant to the implementation.

Possible categories include:

### Functionality

Does it correctly satisfy the Jira requirements and acceptance criteria?

### Gameplay / Fun

Is the implemented gameplay enjoyable, satisfying, understandable, and appropriate for Trackstorm?

### Controls / Responsiveness

Are inputs responsive, predictable, and intentional?

### Vehicle / Movement Feel

Evaluate weight, momentum, acceleration, braking, steering, grip, sliding, collision response, recovery, and sense of speed.

### Physics

Look for instability, clipping, tunneling, jitter, unrealistic impulses, inconsistent collisions, and exploitable behavior.

### Networking

Evaluate authority, synchronization, prediction, reconciliation, interpolation, latency behavior, packet-order handling, disconnect behavior, and consistency between peers.

### Camera

Evaluate framing, smoothing, responsiveness, visibility, obstruction, orientation, and perception of speed.

### Visual Quality

Evaluate models, materials, textures, lighting, scale, composition, readability, visual hierarchy, and cohesion.

### Animation / Motion

Evaluate transitions, timing, interpolation, procedural motion, impact motion, and visual continuity.

### VFX / Feedback

Determine whether important events such as hits, damage, weapons, pickups, boosts, destruction, and kills are clearly communicated.

### Audio

Evaluate timing, impact, volume, layering, repetition, spatialization, and usefulness as gameplay feedback.

### UI / UX

Evaluate readability, hierarchy, screen usage, discoverability, feedback, interaction flow, and unnecessary friction.

### Game Feel / Juice

Evaluate impact feedback, particles, camera response, audio response, timing, hit reactions, motion, anticipation, and recovery.

More effects do not automatically mean better game feel.

### Performance

Look for frame-time problems, unnecessary allocations, excessive processing, expensive physics operations, resource misuse, network scaling problems, and obvious scalability issues.

### Stability

Look for crashes, exceptions, warnings, invalid states, lifecycle problems, initialization issues, cleanup failures, and inconsistent state.

### Code Quality

Evaluate readability, maintainability, naming, complexity, duplication, coupling, unnecessary abstraction, and consistency.

### Testing / Reliability

Evaluate unit tests, edge cases, regression protection, state transitions, boundary conditions, failure paths, and deterministic behavior.

### Architecture

Ensure the implementation respects Trackstorm's Core/Client separation and dependency rules.

### Integration

For Story critiques especially, evaluate whether separately implemented Tasks work together correctly as one coherent feature.

---

# Trackstorm Architecture

## Trackstorm.Core

Core contains authoritative game/domain behavior that should be:

- Serializable where required.
- Deterministic where practical.
- Multiplayer/synchronization-friendly.
- Independent from Godot.
- Independently unit-testable.
- Usable without rendering or a scene tree.

Core includes, as applicable:

- Gameplay state.
- Gameplay rules.
- Validation.
- Calculations.
- Data structures.
- Simulation logic.
- Synchronization-relevant state.
- Multiplayer authority rules.
- Configuration affecting gameplay.
- State machines.
- Serialization/network contracts.

Core must never reference:

- Trackstorm.Client.
- Godot.
- Nodes.
- Scenes.
- Rendering.
- UI.
- Camera.
- Audio.
- Local device input.
- Scene-tree lifecycle.

Core-owned public contracts must not expose Godot runtime types.

## Trackstorm.Client

Client handles Godot-facing responsibilities such as:

- Rendering.
- Scenes and Nodes.
- VFX.
- Audio.
- UI.
- Camera.
- Presentation.
- Local device input capture.
- Interpolation presentation.
- Godot lifecycle behavior.
- Runtime adapters.

Client may request actions from Core and render Core state.

Client must not independently own or decide authoritative gameplay outcomes.

During every critique ask:

> "Did I place each responsibility in the correct layer?"

Do not place gameplay logic in Client simply because doing so is convenient.

---

# Scope Discipline

Critique the implementation within the scope of the current Jira issue.

Remember:

**1 Jira Story = 1 Story Branch.**

**1 Jira Task/Subtask = 1 Task Branch off the Story Branch = 1 PR.**

The critique does not authorize unrelated feature development or broad refactoring.

If Astra discovers an improvement outside the current Jira scope, report it separately as an:

**Out-of-Scope Recommendation**

Do not implement it without human approval.

A Story critique may identify work that requires a new corrective Task. That recommendation does not authorize Astra to create or implement the corrective Task unless the human approves it.

---

# Critique Output

After implementation and verification, Astra must produce:

## Astra Self-Critique — Task Round X

or:

## Astra Self-Critique — Story Round X

depending on the current review level.

Include:

**Overall Score: X.X / 10**

**Quality Assessment: FAIL (<6) / PASS (>=6)**

### Category Scores

List only materially relevant categories and their scores.

### What Was Verified

Briefly state what was actually:

- Built.
- Tested.
- Run.
- Played.
- Inspected.
- Network-tested.
- Visually inspected.
- Audibly verified.
- Integration-tested.

Clearly distinguish VERIFIED, INFERRED, and UNVERIFIED findings where necessary.

For a **FAIL** score below 6.0, also include:

### Problems Found

For each meaningful issue provide:

**Problem:** What is wrong.

**Evidence:** What testing, observation, or code inspection exposed it.

**Severity:** Critical / High / Medium / Low.

**Impact:** Why it matters technically or to the player.

**Suggested Improvement:** What Astra recommends changing.

**Scope:** In Scope / Out of Scope.

For Story critiques, also state:

**Corrective Work Type:** Existing Task / New Corrective Task / No Code Change Required.

### Recommended Next Round

Explain what Astra would change if authorized to perform another round.

State which issues should be prioritized and why.

Do not assume another round will be authorized.

For a **PASS** score of 6.0 or higher, Problems Found, Suggested Improvement, and Recommended Next Round sections are not required. Do not invent improvement work to populate them.

### Verification Limitations

State anything that could not be directly tested or observed.

---

# Threshold-Based Recommendation Rule

Apply this rule exactly:

```text
Score < 6.0  → recommendation required
Score >= 6.0 → PASS, no recommendation required
```

If the overall score is below 6.0:

- The implementation is **FAIL**.
- Provide at least one meaningful recommendation.
- Every required recommendation must be evidence-based, relevant to the current Jira scope, and specific enough to act on.
- Stop after reporting the score, problems, and recommendations.
- Do not automatically implement any recommendation. The human decides whether another improvement round occurs.

If the overall score is 6.0 or higher:

- The implementation is **PASS**.
- No recommendation is required, and recommendations should normally be omitted.
- Do not search for or invent minor criticism, polish ideas, or low-value improvements merely to generate a recommendation.
- Stop after reporting the passing score and relevant verification results.
- The human may still explicitly request additional work or another critique round.

The mandatory human gate applies regardless of the score.

---

# Mandatory Stop

**After presenting the critique, STOP.**

Do not implement suggested improvements.

Do not begin another critique round.

Do not modify code based on your own recommendations.

Do not create corrective Tasks automatically.

Do not create unrelated follow-up work automatically.

Wait for explicit human approval.

This applies even if:

- The score is below 6.
- The implementation failed the quality assessment.
- A problem appears easy to fix.
- Astra believes the solution is obvious.
- Another critique round would probably improve the score.
- A Story integration problem appears severe.
- A Task PR has not yet been created.
- The implementation already passes every automated test.

The human is the gate between rounds.

---

# Human-Authorized Improvement Round

If the human explicitly authorizes another round, Astra may proceed.

Examples of valid authorization:

> "Proceed with Round 2."

> "Fix the issues you identified and critique again."

> "Fix items 1 and 3, then rerun the critique."

> "Proceed with all in-scope recommendations."

For a Task:

**Approved Fixes → Implement → Build/Test → Run/Inspect → Self-Critique → New Score → Recommendation(s) if FAIL → STOP**

For a Story:

**Approved Corrective Work → Jira Task/Task Branch → Implement → Task Critique → Task PR → Merge to Story → Story Verification → Story Self-Critique → New Score → Recommendation(s) if FAIL → STOP**

Human authorization for Round 2 does **not** automatically authorize Round 3.

Every improvement round requires separate human approval.

---

# Maximum Three Critique Rounds

A Task may have at most:

**3 Task critique rounds.**

A Story may independently have at most:

**3 Story critique rounds.**

Task critique rounds and Story critique rounds are counted separately.

Example Task:

### Task Round 1

Implementation completed.

Astra tests and critiques it.

**Score: 4.9/10**

Astra recommends several improvements.

**STOP — Await human decision.**

Human:

> "Proceed with Task Round 2 and address the in-scope recommendations."

### Task Round 2

Astra implements approved improvements.

Astra rebuilds, tests, and critiques the implementation.

**Score: 5.8/10**

Astra reports at least one meaningful recommendation based on the identified weaknesses.

**STOP — Await human decision.**

Human:

> "Proceed with Task Round 3."

### Task Round 3

Astra performs the approved improvements.

Astra rebuilds, tests, and performs the final Task critique.

**Score: 6.4/10 — PASS**

**STOP.**

No Task Round 4 exists.

A Story follows the same three-round maximum independently.

---

# Scores Must Be Independent

Do not assume a later round deserves a higher score.

For example, this is valid:

**Round 1: 5.1**

**Round 2: 5.7**

**Round 3: 5.4**

A later round may expose regressions or previously unnoticed weaknesses.

Score what currently exists.

Do not score effort.

Do not score the number of changes made.

Do not score how many tests were added.

Do not reward a later round simply because it is later.

Score the quality of the resulting implementation.

---

# Task Completion and Pull Request Timing

The critique occurs **before the Task is considered accepted for PR completion**.

Recommended Task lifecycle:

```text
Story branch updated
        ↓
Task branch created/updated
        ↓
Implement
        ↓
Build/Test/Inspect
        ↓
Task Critique
        ↓
Score + Recommendation(s) if FAIL
        ↓
STOP
        ↓
Human decision
        ↓
Optional authorized improvement round(s)
        ↓
Human accepts Task
        ↓
Final verification
        ↓
Push Task branch
        ↓
Create Task PR → Story branch
```

Do not create additional implementation changes after the human accepts the final critique unless separately authorized.

Follow `docs/workflow.md` for exact branch and PR requirements.

---

# Story Completion and Pull Request Timing

The Story critique occurs after all required Task PRs are merged into the Story branch and before the Story is considered ready to merge into `main`.

Recommended Story lifecycle:

```text
All required Task PRs merged
        ↓
Story branch updated from main
        ↓
Full Story verification
        ↓
Story Critique
        ↓
Score + Recommendation(s) if FAIL
        ↓
STOP
        ↓
Human decision
        ↓
Optional corrective Task(s)
        ↓
Corrective Task critique(s)
        ↓
Corrective Task PR(s) → Story branch
        ↓
Optional authorized next Story critique round
        ↓
Human accepts Story
        ↓
Final Story verification
        ↓
Story PR → main
```

Do not apply Story critique fixes directly to the Story branch.

---

# Human Authority

The score is advisory.

The human reviewer has final authority.

The human may decide that:

- 5.7 is acceptable for the milestone.
- A passing score still needs another round or specific additional work.
- One recommendation from a failing critique should be implemented but another should not.
- An identified issue belongs in another Jira Task.
- A Story issue requires a corrective Task.
- An issue should be deferred.
- The implementation should be reverted or redesigned.
- No additional round is necessary.

Astra provides evidence and the recommendations required for a failing score.

**The human makes the decision.**

---

# Prime Directive

**Astra is reviewing Astra.**

Treat your own implementation as if another developer submitted it.

Do not defend your work.

Do not reward yourself because it compiles.

Do not reward yourself because tests pass.

Do not search for reasons to reach 6.

Search for real problems.

A merely functional implementation is approximately:

**5 / 10**

A solid implementation appropriate for the current Trackstorm milestone begins at approximately:

**6 / 10**

After every Task implementation, Story integration, or human-authorized improvement round:

**Critique → Score → Recommendation(s) if FAIL → STOP**

Never automatically:

**Critique → Fix → Critique**

The human controls that transition.

**Maximum: 3 critique rounds per Task.**

**Maximum: 3 critique rounds per Story.**

The objective is not to achieve a particular score.

The objective is to give the human reviewer an accurate assessment of Trackstorm's quality and allow the human to decide whether additional polish, corrective work, or integration changes are worth the cost and scope.
