# TS-76 follow-up independent engineering review

Review date: 2026-09-23. Review target: uncommitted approved follow-up on `ts-76-pg`, PR 90. Final runtime evidence and post-push delivery review are pending.

## Scope and method

Read repository routing, workflow, engineering standards, Client routing, relevant map/vehicle documentation and asset provenance. Inspected the follow-up source diff, Blender authoring scripts, Godot import hooks, imported asset settings and native verification changes. Ran `git diff --check` successfully. Did not run concurrent builds or gameplay checks and did not alter production files.

Executed three read-only Blender 5.2.2 source evaluations, stopping before mesh generation/export. The second evaluation inspected the shared-shoulder terrain formula; the third included the final tapered toe. Each loaded the retained graybox in memory, evaluated the generator's existing point grid and height arrays, and sampled nearest authored vertices. These measurements are source-array evidence, not native collision or player observation.

## Findings and resolved iteration issues

- Existing framework dimensions and 6.35 m deck height are retained. The ten jump-facing parapet pieces are removed. Nine visible deck panels remain editable, with a continuous Blender-authored `DeckRoad-colonly` slab replacing their individual collision surfaces. Export still evaluates source bevel modifiers. No Godot CSG substitute is introduced.
- Deck and perimeter beam collision receives the existing upward-normal terrain contact classification; supports and barriers retain obstacle classification. Production Core physics is unchanged.
- An intermediate hard maximum against the original terrain broke the claimed core-side symmetry by 0.583 m at the sampled X=45, Z=±23.25 m location and introduced a slope crease. That formula was replaced before final verification.
- The formula mirrors the positive existing hill envelope into the shared shoulder and uses a compact smooth maximum before the existing outer blend. The intermediate constant-width toe changed 55,376 vertices. The **final tapered toe changes 54,980 vertices, lowers zero vertices, and has a maximum local fill height of exactly 6.35 m**, independently confirmed by the third read-only evaluation. Generator assertions confirmed unchanged vertices at |X|≥83 m and |Z|≥28.25 m, preserving the kicker faces and outer terrain footprint.
- At sampled X=15, 40, 45 and 65 m, paired Z=±14, ±18 and ±22 m heights matched exactly. At nominal Z=±23.25 m, nearest-vertex measurements differed by at most 0.002 m. The shared completed approaches are symmetric; this does not assert identical preserved surroundings.
- The outer five-metre collar returns to original asymmetric terrain. At X=45 m, measured side differences were 0.086 m at |Z|=24, 0.448 m at 25, 0.733 m at 26 and 1.499 m at 28; these positions are unaffected by the final toe taper. The post-kicker longitudinal transition also retains baseline influence. Documentation must restrict exact symmetry claims to the completed flat approach region (10.75≤|X|≤70 m) and identify both tie-in regions.
- Final verification exposed a new northwest basin-start regression beside the constant-width toe. The final correction smoothly narrows the outer limit from 28.25 m at |X|=70 m to 23.25 m at the unchanged |X|=83 m kicker lip, retaining the five-metre blend width. Independent source evaluation found **zero changed vertices** in the sampled basin-start rectangle |X+77|<2 m, |Z+29|<1.4 m. Final core-side paired sample difference remains at most 0.001712 m. Native recovery results are reported separately by the implementation harness.
- Verification exercises both upper directions, multiple deck offsets, low-speed full approaches, four side climbs, preserved lower passage and deliberate support impacts. Final runtime results must establish practical climbability and absence of damaging catch points; source inspection alone cannot establish those outcomes.

## Pending final review

No unresolved production source defect was identified in the frozen geometry. Comprehensive final verification, native results, final documentation, current-main/version state, pushed commit and PR delivery state remain to be reviewed before a final engineering verdict. User-owned changes to `code/Core/Input/InputButtons.cs` and `assets/maps/oval/grass_variation.png.import` are outside this review's implementation scope and must remain excluded from the Story commit.
