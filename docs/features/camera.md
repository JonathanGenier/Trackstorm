# Player Vehicle Chase Camera

## Behavior and Orientation

Practice and network gameplay share Client.Vehicles.VehicleChaseCamera. In normal chase view, camera yaw follows the already-interpolated/displayed vehicle heading directly on every update, with a level horizon and a fixed downward viewing angle. There is no additional heading smoothing or independent yaw catch-up period. Invalid, near-vertical or overturned orientations retain the last usable heading; a usable displayed heading is adopted immediately on recovery. Positional inertia provides motion and weight. New vehicle identities and life generations reset follow and feedback memory immediately.

Holding RMB enables mouse orbit around the local displayed vehicle; releasing it smoothly returns behind the vehicle. The existing remappable right-stick camera intent also orbits, and neutral stick returns automatically. RMB holds the current offset even without movement; mouse and stick motion can combine. Steering alone cannot rotate the camera. With no free-look offset, displayed heading determines chase yaw immediately, preserving the existing heading follow and positional inertia.

Mouse sensitivity is 0.003 radians per screen-relative pixel, independent of render duration and viewport stretching. Full stick speed is 2.2 radians/second, with diagonal magnitude limited to one. Camera analog bindings use at least a 0.15 dead zone (or the larger configured input dead zone), so reducing driving dead zone cannot disable camera noise rejection. Yaw wraps around the vehicle; pitch stays between 5 and 75 degrees downward to preserve a level horizon and avoid flipping through the poles. Exponential recentering at 6/s follows the current displayed heading while returning. The chase distance/height define the orbit radius around the existing 0.5 m aim height; inertia remains in the vehicle-heading frame and does not redirect aim.

`PlayerInput` retains sole mouse-capture ownership and collects RMB-gated screen motion. Its existing focus, menu, diagnostic and arena gates suppress camera input and discard queued mouse motion. One render follow consumes the accumulated motion exactly once. Vehicle identity/life changes discard orbit and queued motion; network resynchronization explicitly resets the same camera even when identity/life remain unchanged. A reset first presents the chase baseline; subsequent fresh input can orbit again. Arena reconstruction starts a fresh camera.

## Positional Inertia and Feedback

Measured world velocity is sampled once per newer vehicle movement snapshot. Velocity differences use elapsed physics ticks, rather than render duration, to estimate acceleration. Render-time exponential damping blends bounded offsets in the heading-relative frame: forward acceleration moves the camera slightly rearward, braking/deceleration moves it forward, and lateral acceleration moves it toward the outside of a turn. Actual sideways velocity adds restrained lateral weight during sliding/drift. Constant straight velocity removes the acceleration response; neutral sideways velocity removes the slip response. No raw steering, drift button or vehicle command drives these effects.

Horizontal follow uses the displayed position plus the chase offset and bounded inertia. Vertical anchor damping absorbs bumps. Default fore/aft offset is limited to 0.7 m and lateral offset to 0.4 m. There is no unbounded world-space horizontal lag or additional FOV animation.

Local contacts above the configurable severity threshold produce a small vertical impulse. Severity uses normal contact velocity or impulse divided by mass. Accepted damage outcomes trigger HP-scaled feedback, deduplicated by sequence within the vehicle life. Network contacts already presented at a prediction tick are not replayed by reconciliation. Impulses coalesce using the strongest envelope, with a short contact cooldown and exponential decay. Feedback cannot change vehicle forces, HP, heading or camera aim.

## Tuning and Architecture

**Settings → Gameplay → Camera shake** scales collision/damage displacement from 0–100%, defaulting to 100% to retain the established restrained response. The local [Settings controller](settings.md) persists the preference and supplies it to both practice and network cameras, including reconstructed arenas. Scaling is applied after the bounded envelope; it never changes follow, heading, positional inertia, input, vehicle state or network configuration. Zero clears pending shake while preserving contact cooldown and consuming damage sequence IDs. Re-enabling starts with no queued disabled impact. The default maximum displacement remains 0.12 m, with real events normally below that bound.

The ChaseCamera node exposes Inspector properties for follow distance/height (approximately 15.096 m / 6.862 m, preserving framing after vehicle rescaling), position damping (8/s), longitudinal/lateral acceleration gains (0.045 / 0.025 metres per m/s²), sideways-speed gain (0.015 metres per m/s), maximum inertia offsets, collision/damage gains, contact threshold, shake decay and maximum vertical shake (0.12 m). Live Remote Inspector tuning is supported; scene-authored instances can save exported values. Current arenas instantiate shared defaults.

Practice native bodies use Godot physics interpolation; the camera reads their interpolated pose. Network vehicles retain explicit pose interpolation/correction smoothing; the camera reads the visual pose after presentation updates. Automatic interpolation is disabled for the render-updated camera. Camera state remains entirely in Client, outside Core, input frames, prediction history and network payloads. Physics and vehicle input behavior are unchanged.

## Verification and Limits

Pure Client tests cover orbit/recenter consistency at 30/60/144 FPS, pitch/yaw bounds, mouse hold/reset behavior, acceleration/braking, lateral inertia, settling and feedback. `check-camera.ps1 -GodotPath <exe>` runs native synthetic mouse/stick events through the production input owner and camera: RMB orbit/hold/release, noise rejection, gameplay-frame isolation, suppression, identity/life/reseed reset, immediate heading follow, inertia, feedback and unstable-orientation recovery. The fast-check router includes this harness. Native vehicle and network-vehicle checks additionally exercise mouse/controller orbit and smooth return on the actual displayed-pose path, saving phase screenshots when rendered. Practice checks include native life reset; reconnect checks hold an orbit across production resume on a reused vehicle.

Synthetic inputs and screenshots do not establish subjective driving comfort or motion polish. The prototype still has no camera obstruction avoidance; nearby structures can obscure the vehicle. Spectator, replay, interior and replicated cameras are not implemented.

[Feature index](README.md)
