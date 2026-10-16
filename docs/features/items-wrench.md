# Wrench robotic repair arm

Wrench healing, immediate consumption, activation validation and network item
ownership remain in Core. The Client rig only presents accepted state.

`WrenchArm` is a full-size child of the production `WeaponRack`, under the same
interpolated vehicle root as the chassis. `CarRackPresentation` retains selection,
capability, accepted-use and life ownership. Existing `CarDeployment` opens the
split trunk and raises the rack; the arm folds before that rack can descend.

The editable Blender assembly has a rigid shoulder, 0.70 m upper link and a
1.25–3.25 m telescopic forearm during deployment, extending to 3.45 m for the
forward repair reach. Nested Slide and Reach transforms translate rigid
sections without stretching the meshes. Rotary unions and fixed section lines
avoid hoses bridging moving joints. The full-size power pack and horizontal reservoir mount beneath
the existing rack, with the tank above the battery. Following the Oil/Nitro
presentation policy, this fixed service assembly is hidden while stowed and
revealed when the original rack reaches Y=0.75 m, giving its bottom clearance
above the bay floor. It is not physically packed beneath the lowered rack.
The articulated arm remains full-size and physically folds into the trunk;
only the auxiliary services use clearance-gated visibility. No additional lift,
service scale animation or network state is introduced. Additional decorative
dressing is absent.

Deployment first raises the collapsed arm, then extends above the roof. The
ready tool hovers ahead of the windshield over the hood. `HoodContact(index)`
exposes rack-local left/center/right tip positions at x=-0.50/0/+0.50 m,
vehicle z=-1.98 m, on the production bonnet's sloping surface 14 cm behind its
front edge. Each existing telescopic section travels another 10 cm during this
forward reach to clear the roof lamps, then returns before the original stow path.
`RequestHoodPose(index)` requests one contact; null returns to hover. Lateral
changes lift before traversing. Retraction lifts clear, centers, telescopes and
folds. All targets are vehicle-local and remain attached while driving.

Use is rejected during deployment, without queuing the early press. Core requires
a fresh shared-rack deployment plus the 1.8-second arm deployment for the exact
life, capability and selection. This transient readiness is reset on recovery.
Local input additionally waits for the actual outgoing payload to return and the
selected arm to finish unfolding. No new packet or inventory field is added.

One accepted use immediately applies the existing authoritative heal and consumes
one Wrench. Its existing reliable outcome starts a 0.96-second cosmetic sequence
on owner and observers: lift, left contact, lift, center contact, lift, right
contact, lift. Lateral travel happens clear of the bonnet. Each contact holds for
about 0.106 seconds with chuck rotation, at most 1 mm of upward vibration and one
brief spark burst. The raised tool then follows the original centering, folding,
rack lowering and trunk closing path. The deployment and final stow are separate
from the repair duration. A contact never applies another heal.

The bounded spark effect uses ten small emissive flecks by default, at most sixteen,
with a 0.16-second lifetime. Its origin is the actual vehicle-local tip contact;
upward flight makes the small streaks visible beyond the hood from behind. It
adds no textures, external assets or sound. The former vehicle-origin Wrench VFX
is suppressed. Existing generic audio behavior is unchanged.

DevTools exposes session-local duration (0.8–1 seconds), contact hold fraction
(0.25–0.40 per beat), and spark intensity (0–2). Settings are captured for the next
repair; healing and deployment balance are not duplicated. Each accepted token
plays at most once. An accepted repair owns the rack through all contacts, arm
nesting, rack descent and complete trunk closure. Pickups and selection changes
update the pending accepted inventory without interrupting this lifecycle. The
incoming capability remains valid, but local use and discard wait for the mechanism
to close. Selection changes remain accepted as pending intent.
The HUD projects the installed spent Wrench in its physical slot until that same
handoff completes; it has no independent delay or predicted inventory mutation.
Core still heals and consumes immediately, leaving capacity available for pickups.
An unused Wrench can be switched/discarded normally without repair. Reseed and
life changes discard animation memory.
Sparks are owned by the arm, cleared on interruption/completion, and their meshes
and materials are disposed when the arm leaves the tree.

`check-wrench-arm.ps1 -GodotPath <editor> [-Visual] [-Impaired]` exercises two
native UDP peers. Independent pose requests retain the approved geometry regression;
real activations additionally verify ordered contacts, timing, sparks, one-use
healing, early rejection, moving cars, uneven terrain, collision, switching,
discard, recovery, and death with active effects. Its repeated pickup matrix uses
the real pickup authority before use, immediately after use, during drilling and
during retraction, stopped and moving, with actual HUD and both-peer checks.
Rendered evidence and the text
record go to `.godot/ts293`. Surrounding item handoffs are
covered by `check-car-rack.ps1`. Neither fixture establishes physical-PC/EOS
or Internet behavior.
