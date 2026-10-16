# Wrench robotic repair arm

Wrench healing, immediate consumption, activation validation and network item
ownership remain in Core. The Client rig only presents accepted state.

`WrenchArm` is a full-size child of the production `WeaponRack`, under the same
interpolated vehicle root as the chassis. `CarRackPresentation` retains selection,
capability, accepted-use and life ownership. Existing `CarDeployment` opens the
split trunk and raises the rack; the arm folds before that rack can descend.

The editable Blender assembly has a rigid shoulder, 0.70 m upper link and a
1.25–3.25 m telescopic forearm. Nested Slide and Reach transforms translate rigid
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
vehicle z=-1.75 m, on the production bonnet's sloping surface ahead of its scoop.
`RequestHoodPose(index)` requests one contact; null returns to hover. Lateral
changes lift before traversing. Retraction lifts clear, centers, telescopes and
folds. All targets are vehicle-local and remain attached while driving.

The rig does not run an automatic three-contact activation sequence, drill
vibration, spin, repair particles or audio. A short accepted-use cue preserves
existing presentation/lifecycle integration pending the separate animation work.
Pose requests never heal, consume items or send network events. Both owner and
observers reconstruct selection and accepted use through the existing item
publications. Reseeding does not replay old use; death clears the payload.

`check-wrench-arm.ps1 -GodotPath <editor> [-Visual] [-Impaired]` exercises two
native UDP peers. The fixture requests the three poses independently to verify
hood reach and geometry; these fixture requests are not production activation.
It also checks trunk fit, articulation, lifecycle, healing, driving and collisions.
Rendered evidence goes to `.godot/ts292/round3`. Surrounding item handoffs are
covered by `check-car-rack.ps1`. Neither fixture establishes physical-PC/EOS
or Internet behavior.
