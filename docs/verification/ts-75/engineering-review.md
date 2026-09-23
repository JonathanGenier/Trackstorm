# TS-75 independent engineering review

Reviewed `ts-75-pg` at pushed commit `965f2f1` against `origin/main`
`6b4c42d`, including the pending LF-only JSON generation/hash maintenance.
Review date: 2026-09-23. This is the engineering/delivery review; Astra owns
the separate runtime/experiential verdict.

## Authority and scope

Read workflow, applicable engineering standards, Client routing, map/vehicle
feature documentation, terrain authoring sources, production scene changes,
native fixture changes, provenance records and the recorded final evidence.
Read current Jira TS-75 and TS-74 descriptions, comments and child lists directly.
Neither Story has child tasks. TS-74 is the completed dependency. TS-75's Epic
parent does not authorize implementing other map Stories.

The live TS-75 description explicitly preserves the user instruction to keep
this work terrain-only and report the existing slope-start blocker. It does
not waive acceptance. It also now specifies Carnage Circus jump spectacle and
that current landing damage must not constrain appropriate jump design;
TS-141 and TS-142 are follow-up work, not permission to leave terrain defects.
The example jump dimensions are directional, not mandatory numeric bounds.

## Findings

1. **P1 — Known acceptance blocker, not a new terrain implementation defect.**
   `code/Core/Vehicles/VehicleMovement.cs:131` selects braking whenever throttle
   opposes even tiny negative longitudinal motion. The new recovery scenarios
   at `code/Client/Verification/InfieldIntegrationChecks.cs:183` and `:188`
   expose supported uphill standing-start failures. The full final native
   infield run fails at `BasinRecovery-85-28`; both outward bank-start runs
   fail as well. Four supported wheels, near-zero speed and a mild grade
   distinguish this from absent collision or an impossible terrain wall.
   Continuous traversal evidence does not establish reliable restart/recovery.
   Keep the Story/PR unaccepted until an authorized vehicle correction and
   affected native re-verification, or an explicit acceptance-criteria decision.
   Do not fix Core under the current TS-75 authorization.

2. **P3 — Nonblocking targeted-harness robustness.**
   `code/Client/Verification/InfieldIntegrationChecks.cs:228` skips every
   unmatched name, while `:207` still reports success. A misspelled nonempty
   `-Case` can therefore pass geometry checks with no driving case executed.
   Consider counting selected cases and failing when a nonempty filter matches
   none. This does not invalidate the recorded runs: their evidence names the
   actual executed driving scenarios, and the full suite correctly fails.

No additional material production-code, terrain integration, architecture or
asset-provenance defect was identified. The known blocker alone prevents a
delivery-ready verdict.

## Engineering evidence

- The scene replaces the active flat support surface with the imported terrain
  and hides the retained foundation visual. Both the baker and committed scene
  select the same terrain asset. Negative basins therefore have no stacked
  foundation collider.
- The unchanged route manifest, TS-74 generator, original oval GLB and road
  collision have no committed diff against main. The terrain retains the
  measured 916-vertex boundary and authors its collar from the road grade.
- The committed branch has no Core diff. Client verification and authored map
  assets retain the existing Client-to-Core dependency direction. Unrelated
  local InputButtons and grass import edits were excluded from this review and
  left untouched.
- Independently recomputed all four current manifest SHA-256 values: Blender
  source, GLB, terrain JSON and layout JSON match. Pending explicit LF JSON
  output is appropriate for the repository's `eol=lf` policy and fixes the
  manifest's clean-checkout byte consistency without changing geometry.
- The existing `check.log` records warning/error-free builds and 540 Core plus
  352 non-native transport tests. Native evidence records the 3,087 oval
  checks, separate-process networking and startup passes, six actual jump
  launches/descending landings and the reported recovery failures. These suites
  were reviewed, not rerun by this reviewer.
- Terrain-normal transition-speed measurement is a reasonable correction to
  the old flat-floor world-Y assumption. Changed numeric bounds are disclosed;
  continuous support and angular-speed limits remain enforced.
- The branch contains the supplied current main commit, version metadata is
  consistent with the documented 0.1.35 Story version, and reviewer
  `git diff --check` passed. No extra PR or merge was performed.

## Delivery and evidence limits

The live Jira now has a TS-141 sequencing comment and expanded spectacle/damage
requirements; the initial report statement that both comment lists were empty
describes an earlier read and must not be treated as current Jira coverage.
Synchronize the delivery report with this current requirement snapshot and
Astra's assessment. The recorded peak vehicle-origin heights of 4.35–5.04 m
and 0.87–1.30 s flights establish physical launches, not acceptance of the
subjective spectacle requirement. The no-damage assertions are observable
results, not authority to shrink geometry solely to avoid landing damage.

The parent reports PR #87 is draft. A direct reviewer `gh pr view` request was
blocked by sandbox network access, so current remote CI status was not verified
independently. Passing CI would not override the known native acceptance failure.
The pending mechanical hash fix and review evidence still need inclusion in the
existing Story delivery. Human acceptance of the separate critique remains
required; this review neither supplies nor implies that acceptance.

## Final verdict

CHANGES REQUIRED
