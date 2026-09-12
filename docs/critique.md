# Astra Self-Critique & Human-Gated Quality Review

## Purpose

Every implementation performed by Astra for Trackstorm must undergo a structured self-critique before the work is considered ready for human review.

Astra must evaluate its own implementation as though it were an independent professional game-development team reviewing work submitted by another developer.

The purpose is to expose weaknesses, technical problems, gameplay issues, missing polish, and opportunities for improvement.

**Astra critiques. The human decides whether Astra proceeds with the suggested improvements.**

Astra must never automatically begin another improvement round after completing a critique.

---

# Core Workflow

For every implementation task, use this workflow:

**Implement → Build/Test → Run/Inspect → Self-Critique → Score → Recommendations → STOP**

Astra must then wait for explicit human instruction.

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

- build failures
- crashes
- major missing functionality
- severe regressions
- fundamentally incorrect behavior

### 3–4 — Poor

The implementation partially works but significant problems remain.

### ~5 — Merely Functional

**A feature that merely works should receive approximately 5/10.**

Compilation, passing tests, or technically satisfying acceptance criteria does not automatically justify a score above 5.

A 5/10 implementation may be completely functional while still lacking refinement, robustness, usability, game feel, or polish.

### 6–7 — Solid

Reliable, coherent, tested, and appropriate for the current Trackstorm milestone.

**6.0/10 is considered a passing implementation.**

### 8–9 — Highly Polished

Professional, intentional, refined, and difficult to substantially improve without additional scope.

### 10 — Exceptional

Reserve 10/10 for implementations where meaningful improvements are extremely difficult to identify within the intended scope.

Never use 10/10 casually.

---

# Passing Score

The Trackstorm quality target is:

**6.0 / 10**

This score is informational for the human reviewer.

A score below 6 does **not** authorize Astra to automatically modify the implementation.

Likewise, reaching 6 does not prevent the human from requesting additional improvements.

**The human controls whether another round occurs.**

Never inflate, manipulate, or round a score simply to reach 6.

---

# Mandatory Critique After Implementation

Whenever Astra completes a meaningful implementation, Astra must immediately perform **one self-critique round**.

Astra should mentally separate itself from the implementation.

Assume another developer wrote the code.

Ask:

> "If another developer submitted this implementation to a professional game-development team, what would I criticize?"

Do not defend previous implementation decisions.

Search for weaknesses.

---

# Verify Before Critiquing

The critique should be based on actual evidence wherever possible.

Depending on the task:

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

For gameplay work, play/test the feature when the environment permits.

For visual work, inspect the rendered result.

For physics work, observe the actual simulation.

For UI work, inspect the UI at runtime.

For audio work, verify playback when technically possible.

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

Does it correctly satisfy the task and acceptance criteria?

### Gameplay / Fun

Is the implemented gameplay enjoyable, satisfying, understandable, and appropriate for Trackstorm?

### Controls / Responsiveness

Are inputs responsive, predictable, and intentional?

### Vehicle / Movement Feel

Evaluate weight, momentum, acceleration, braking, steering, grip, sliding, collision response, recovery, and sense of speed.

### Physics

Look for instability, clipping, tunneling, jitter, unrealistic impulses, inconsistent collisions, and exploitable behavior.

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

Look for frame-time problems, unnecessary allocations, excessive processing, expensive physics operations, resource misuse, and obvious scaling problems.

### Stability

Look for crashes, exceptions, warnings, invalid states, lifecycle problems, initialization issues, and inconsistent state.

### Code Quality

Evaluate readability, maintainability, naming, complexity, duplication, coupling, unnecessary abstraction, and consistency.

### Testing / Reliability

Evaluate unit tests, edge cases, regression protection, state transitions, boundary conditions, and failure paths.

### Architecture

Ensure the implementation respects Trackstorm's Core/Client separation.

---

# Trackstorm Architecture

## Trackstorm.Core

Core should contain logic that should be:

- serializable
- deterministic where practical
- multiplayer/synchronization-friendly
- independent from Godot where practical
- unit-testable
- usable without rendering

This includes gameplay state, rules, calculations, data structures, simulation logic, and synchronization-relevant state.

## Trackstorm.Client

Client handles Godot-facing responsibilities such as:

- rendering
- scenes/nodes
- VFX
- audio
- UI
- camera
- presentation
- input integration
- Godot lifecycle behavior

During every critique ask:

> "Did I place each responsibility in the correct layer?"

Do not place gameplay logic in Client simply because doing so is convenient.

---

# Scope Discipline

Critique the implementation within the scope of the current Jira task.

Remember:

**1 Jira Story = 1 Story Branch.**

**1 Jira Task = 1 Task Branch off the Story Branch = 1 PR.**

The critique does not authorize unrelated feature development or broad refactoring.

If Astra discovers an improvement outside the task's scope, report it separately as an **Out-of-Scope Recommendation**.

Do not implement it without human approval.

---

# Critique Output

After implementation and verification, Astra must produce:

## Astra Self-Critique — Round X

**Overall Score: X.X / 10**

**Quality Assessment:** FAIL (<6) / PASS (≥6)

### Category Scores

List only relevant categories and their scores.

### What Was Verified

Briefly state what was actually built, tested, run, played, or inspected.

### Problems Found

For each meaningful issue provide:

**Problem:** What is wrong.

**Evidence:** What testing, observation, or code inspection exposed it.

**Severity:** Critical / High / Medium / Low.

**Impact:** Why it matters technically or to the player.

**Suggested Improvement:** What Astra recommends changing.

**Scope:** In Scope / Out of Scope.

### Recommended Next Round

Explain what Astra would change if authorized to perform another round.

Estimate which issues should be prioritized and why.

### Verification Limitations

State anything that could not be directly tested or observed.

---

# Mandatory Stop

**After presenting the critique, STOP.**

Do not implement the suggested improvements.

Do not begin another critique round.

Do not modify code based on your own recommendations.

Do not create unrelated follow-up work.

Wait for explicit human approval.

This applies even if:

- the score is below 6
- the implementation failed the quality assessment
- a problem appears easy to fix
- Astra believes the solution is obvious
- another critique round would probably improve the score

The human is the gate between rounds.

---

# Human-Authorized Improvement Round

If the human explicitly authorizes another round, Astra may proceed.

Examples of valid authorization:

> "Proceed with Round 2."

> "Fix the issues you identified and critique again."

> "Fix items 1 and 3, then rerun the critique."

> "Proceed with all in-scope recommendations."

When authorized:

**Approved Fixes → Implement → Build/Test → Run/Inspect → Self-Critique → New Score → Recommendations → STOP**

Astra must then stop again.

Human authorization for Round 2 does **not** automatically authorize Round 3.

Every improvement round requires separate human approval.

---

# Maximum Three Critique Rounds

A task may have at most:

**3 critique rounds.**

For example:

### Round 1

Implementation completed.

Astra tests and critiques it.

**Score: 4.9/10**

Astra recommends several improvements.

**STOP — Await human decision.**

Human:

> "Proceed with Round 2 and address the in-scope recommendations."

### Round 2

Astra implements the approved improvements.

Astra rebuilds, tests, and critiques the new implementation.

**Score: 5.8/10**

Additional weaknesses are identified.

**STOP — Await human decision.**

Human:

> "Proceed with Round 3."

### Round 3

Astra performs the approved improvements.

Astra rebuilds, tests, and performs the final critique.

**Score: 6.4/10 — PASS**

**STOP.**

No Round 4 exists under this process.

---

# Scores Must Be Independent

Do not assume a later round deserves a higher score.

For example, this is valid:

**Round 1: 5.1**

**Round 2: 5.7**

**Round 3: 5.4**

Round 3 may expose regressions or previously unnoticed weaknesses.

Score what currently exists.

Do not score effort.

Do not score the number of changes made.

Do not score how many tests were added.

Score the quality of the resulting implementation.

---

# Human Authority

The score is advisory.

The human reviewer has final authority.

The human may decide that:

- 5.7 is acceptable for the milestone.
- 6.2 still needs another round.
- One recommendation should be implemented but another should not.
- An identified issue belongs in another Jira task.
- The implementation should be reverted or redesigned.
- No additional round is necessary.

Astra provides evidence and recommendations.

**The human makes the decision.**

---

# Prime Directive

**Astra is reviewing Astra.**

Treat your own implementation as if another developer submitted it.

Do not defend your work.

Do not reward yourself because it compiles.

Do not reward yourself because tests pass.

Do not search for reasons to reach 6.

Search for problems.

A merely functional implementation is approximately:

**5 / 10**

A solid implementation appropriate for the current Trackstorm milestone begins at approximately:

**6 / 10**

After every implementation or human-authorized improvement round:

**Critique → Score → Recommend → STOP**

Never automatically:

**Critique → Fix → Critique**

The human controls that transition.

**Maximum: 3 critique rounds per task.**

The objective is not to achieve a particular score.

The objective is to give the human reviewer an accurate assessment of Trackstorm's quality and allow the human to decide whether additional polish is worth the cost and scope.
