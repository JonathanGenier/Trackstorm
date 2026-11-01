# TS-323 performance evidence

Three sequential Debug trials on the same unchanged source; 1280x720 Compatibility renderer, uncapped/VSync off, 60 Hz physics, 180 warmup and 1200 sample ticks per phase. One forced offscreen draw per process frame: these are render-loop intervals, not displayed FPS or GPU timers. Browser/Discord/Codex remained open; only light evidence bookkeeping during measurement, no reboot/thermal/clock control. Sequential scenarios include movement, camera and scene-composition differences, so they are not isolated causal A/B estimates. This is not an exported-build, historical regression or long human combat test.

Hardware reported by the runtime: Intel Core i5-7600K @ 3.80 GHz (four processors), NVIDIA GTX 1070, driver 582.66, Windows 10.0.19045, Godot .NET 4.7.2. Windows power scheme: High performance.

Ranges below are the minimum and maximum of the three run-level measurements, not confidence intervals. See [summary.json](summary.json) and each raw run for sample counts, maximum intervals, frames over 33/50 ms, allocations and full resource observations.

| Phase | Cars | Mean interval ms | p95 ms | p99 ms | 1000 / mean interval, frames/s | CPU core equivalents |
| --- | --- | --- | --- | --- | --- | --- |
| map-only | 0 | 3.71–3.94 | 4.09–4.50 | 4.46–4.94 | 253.7–269.4 | 0.93–0.96 |
| map-pickups | 0 | 4.24–4.47 | 4.73–5.05 | 5.12–5.63 | 223.8–235.6 | 0.94–0.96 |
| crate-pressure | 0 | 4.99–5.59 | 5.56–7.15 | 24.10–25.81 | 178.7–200.3 | 0.88–1.00 |
| crate-cleanup | 0 | 3.73–4.11 | 4.11–4.95 | 4.35–6.12 | 243.5–267.9 | 0.86–0.97 |
| idle-one | 1 | 5.29–5.65 | 6.36–6.85 | 7.93–8.52 | 176.8–189.1 | 0.94–0.96 |
| driving-one | 1 | 5.48–6.73 | 7.33–8.94 | 8.58–12.74 | 148.7–182.5 | 0.95–0.99 |
| three-cars-prior-roster | 3 | 8.48–10.02 | 10.77–13.61 | 17.99–19.39 | 99.8–118.0 | 0.93–1.00 |
| four-cars | 4 | 10.22–12.25 | 12.14–14.57 | 20.31–22.67 | 81.7–97.9 | 0.95–0.99 |
| combat-machine-gun | 4 | 13.50–14.10 | 16.21–18.40 | 22.34–23.28 | 70.9–74.1 | 1.02–1.02 |
| sustained-four-cars | 4 | 11.35–11.98 | 12.85–13.82 | 21.09–21.74 | 83.5–88.1 | 0.97–1.01 |
| five-cars-application-roster | 5 | 11.93–13.17 | 13.76–18.06 | 22.37–23.92 | 76.0–83.8 | 0.94–1.00 |

| Phase | End private memory MB (decimal) | Start nodes | End nodes | End orphans |
| --- | --- | --- | --- | --- |
| map-only | 442.4–444.1 | 1728–1728 | 1728–1728 | 0–0 |
| map-pickups | 515.6–520.6 | 2107–2107 | 2107–2107 | 0–0 |
| crate-pressure | 551.8–572.9 | 2107–2107 | 2377–2377 | 0–0 |
| crate-cleanup | 557.8–571.1 | 2107–2107 | 2107–2107 | 0–0 |
| idle-one | 817.7–828.0 | 2664–2664 | 2664–2664 | 0–0 |
| driving-one | 843.4–935.1 | 2741–2928 | 2730–2917 | 0–0 |
| three-cars-prior-roster | 867.1–945.5 | 3394–3581 | 3447–3591 | 0–0 |
| four-cars | 878.1–958.3 | 3780–3924 | 3856–4078 | 0–0 |
| combat-machine-gun | 902.8–994.4 | 3830–4148 | 3803–4121 | 0–0 |
| sustained-four-cars | 904.1–1007.8 | 3793–4111 | 3793–4111 | 0–0 |
| five-cars-application-roster | 905.4–1028.8 | 4224–4530 | 4214–4520 | 0–0 |

Resource counts are bounded snapshots within these trials, not proof against leaks over long sessions. Repeated crate pressure expires before the cleanup phase. Machine Gun event counts in the raw data prove the combat phase actually fired. The fixture covers one, three, four and five cars; it does not provide an isolated two-car trial or Missile/Salvo/Oil/Mine GPU profiles.

Other measurements are separate workloads: Machine Gun VFX CPU update median/p95 0.306/0.350 ms; impact sample/update CPU 0.954/1.048 ms; headless two-arena Shield deployment physics sample peak 56.199 ms; 32 stationary Boost visual presentations p95 frame interval 57.86 ms with a 60 FPS cap; Boost rollover camera Follow CPU p95/max 0.187/0.991 ms. Do not add these numbers together or attribute them all to the GPU.
