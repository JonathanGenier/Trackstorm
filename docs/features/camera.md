# Player Vehicle Chase Camera

## Behavior and Orientation

Practice and network gameplay share Client.Vehicles.VehicleChaseCamera. Camera yaw follows the already-interpolated/displayed vehicle heading directly on every update, with a level horizon and a fixed downward viewing angle. There is no additional rotational smoothing or independent yaw catch-up period. Invalid, near-vertical or overturned orientations retain the last usable heading; a usable displayed heading is adopted immediately on recovery. Positional inertia provides motion and weight. New vehicle identities and life generations reset follow and feedback memory immediately.

The driving camera has no mouse, right-stick or steering-input dependency. These inputs are not sampled, consumed or assigned replacement behavior by the camera. Steering alone cannot rotate the camera. Only changes in actual vehicle heading change chase yaw. Aim is independent of lateral inertia and shake, so positional weight cannot introduce additional orbit or look-at yaw.

## Positional Inertia and Feedback

Measured world velocity is sampled once per newer vehicle movement snapshot. Velocity differences use elapsed physics ticks, rather than render duration, to estimate acceleration. Render-time exponential damping blends bounded offsets in the heading-relative frame: forward acceleration moves the camera slightly rearward, braking/deceleration moves it forward, and lateral acceleration moves it toward the outside of a turn. Actual sideways velocity adds restrained lateral weight during sliding/drift. Constant straight velocity removes the acceleration response; neutral sideways velocity removes the slip response. No raw steering, drift button or vehicle command drives these effects.

Horizontal follow uses the displayed position plus the chase offset and bounded inertia. Vertical anchor damping absorbs bumps. Default fore/aft offset is limited to 0.7 m and lateral offset to 0.4 m. There is no unbounded world-space horizontal lag or additional FOV animation.

Local contacts above the configurable severity threshold produce a small vertical impulse. Severity uses normal contact velocity or impulse divided by mass. Accepted damage outcomes trigger HP-scaled feedback, deduplicated by sequence within the vehicle life. Network contacts already presented at a prediction tick are not replayed by reconciliation. Impulses coalesce using the strongest envelope, with a short contact cooldown and exponential decay. Feedback cannot change vehicle forces, HP, heading or camera aim.

## Tuning and Architecture

The ChaseCamera node exposes Inspector properties for follow distance/height (11 m / 5 m), position damping (8/s), longitudinal/lateral acceleration gains (0.045 / 0.025 metres per m/s²), sideways-speed gain (0.015 metres per m/s), maximum inertia offsets, collision/damage gains, contact threshold, shake decay and maximum vertical shake (0.12 m). Live Remote Inspector tuning is supported; scene-authored instances can save exported values. Current arenas instantiate shared defaults.

Practice native bodies use Godot physics interpolation; the camera reads their interpolated pose. Network vehicles retain explicit pose interpolation/correction smoothing; the camera reads the visual pose after presentation updates. Automatic interpolation is disabled for the render-updated camera. Camera state remains entirely in Client, outside Core, input frames, prediction history and network payloads. Physics and vehicle input behavior are unchanged.

## Verification and Limits

Pure Client tests cover acceleration/braking direction and smooth transitions, lateral turn/slip response, bounds, settling, rotated reference frames, 30/60/144 FPS consistency, life resets and shake decay. The native camera_checks.tscn harness injects mouse, right-stick and steering events and checks unchanged camera pose at fixed vehicle state, one-update heading alignment across large turns and wraparound at 30/60/144 FPS, retained bounded lateral inertia during an immediate heading change, unstable-orientation fallback and immediate recovery, settling, collision feedback, damage deduplication and life/airborne recovery. Existing driving, network and lifecycle harnesses exercise both gameplay paths.

Synthetic inputs and screenshots do not establish subjective driving comfort or motion polish. The prototype still has no camera obstruction avoidance; nearby structures can obscure the vehicle. Spectator, replay, interior and replicated cameras are not implemented.

[Feature index](README.md)
