# TS-323 questionnaire results — pilot fallback

Observed Tally inventory: 14 sections and A001–A140, each exposing PASS / FAIL / PARTIAL / NOT TESTED. Full audit did not start because the pilot submission was blocked. Totals: 5 PASS, 0 FAIL, 0 PARTIAL, 135 NOT TESTED. These are pilot coverage totals, not a completed full audit. Structured per-question conditions, observations, evidence and limits are in [results.json](results.json).

| ID | Question | Result | Observation / evidence |
| --- | --- | --- | --- |
| A001 | Does the game launch successfully without startup errors? | PASS | Rendered production startup harness passed with exit 0 and no diagnostics: phase-aware recovery, initialization, menu gating and media continuity. [startup-visual.log](startup-visual.log) |
| A002 | Does the main menu load and display correctly? | PASS | Current rendered 1280x720 settled menu inspected: readable Play, disabled Garage/Coming Soon, Settings, Quit and v0.2.75. Startup harness also verified viewport bounds and navigation. [startup-visual.log](startup-visual.log), [menu-settled.png](menu-settled.png) |
| A003 | Can a new gameplay session start and initialize correctly? | PASS | Native menu integration passed 3535 assertions, including a solo UDP gameplay session entering synchronized Countdown. [menu-native.log](menu-native.log) |
| A004 | Can the player exit and restart gameplay without stale state? | PASS | The same native menu run verified Leave returns to Main Menu, reentry starts a fresh Countdown, and production Quit disposes the arena/session. [menu-native.log](menu-native.log) |
| A005 | Does the game recover from vehicle death and respawn? | PASS | Eight native UDP peers completed four alternating Missile/collision deaths. All peers observed Dead/Respawning/Alive; respawn ticks 465, 662, 961, 1158; reset physics/HP/items/VFX and permanent second-slot discard verified. [death-native.log](death-native.log), [death-evidence.txt](death-evidence.txt) |
| A006 | Can the player pause and resume without breaking gameplay? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A007 | Does restarting a session reset the appropriate world and player state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A008 | Are scene transitions free of unexpected errors or missing resources? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A009 | Do repeated gameplay sessions avoid accumulating errors or orphaned objects? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A010 | Does the game remain stable throughout an extended single-player session? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A011 | Do all applicable main-menu and settings controls respond correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A012 | Do keyboard and mouse inputs perform their assigned actions? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A013 | Do physical controller inputs and device switching behave correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A014 | Are the two item slots displayed and updated correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A015 | Do weapon-specific item HUD variants display correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A016 | Are ammunition, cooldown, health and damage indicators accurate? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A017 | Do gameplay and camera settings persist through a restart? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A018 | Does Reset User Settings restore intended defaults? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A019 | Do audio and other available settings apply correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A020 | Are HUD elements readable during driving, aiming and heavy combat? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A021 | Does vehicle acceleration behave consistently with established tuning? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A022 | Do braking and reversing behave correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A023 | Does steering remain responsive at low and high speeds? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A024 | Does the suspension behave correctly on uneven terrain? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A025 | Are jumps and landings physically stable? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A026 | Do banks, ramps and slopes produce predictable vehicle behavior? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A027 | Do collisions with arena structures resolve correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A028 | Do vehicle-to-vehicle collisions produce established damage and movement? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A029 | Do recovery, unstuck and respawn behavior work where implemented? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A030 | Does vehicle handling remain stable while carrying or deploying weapons? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A031 | Does the camera provide smooth full 360-degree movement? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A032 | Are approved camera defaults and player adjustments correctly applied? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A033 | Does entering and exiting aim mode preserve appropriate framing? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A034 | Does the raised driving crosshair remain visible above the weapon? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A035 | Do white, red and green crosshair states accurately represent firing and lock eligibility? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A036 | Does magnetic acquisition engage correctly on stationary and moving targets? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A037 | Does target retention remain stable without excessive stickiness? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A038 | Can mouse and controller users deliberately break away and switch targets? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A039 | Does free-camera mode toggle and restore normal gameplay without altering aim or vehicle authority? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A040 | Do crosshair placement, accepted shot direction and valid vertical aiming remain aligned across applicable weapons? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A041 | Do Carnival Mystery Crates display with their intended model and presentation? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A042 | Does crate destruction produce the approved smash and confetti sequence? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A043 | Does a successful pickup grant the expected item? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A044 | Does pickup correctly handle full item slots? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A045 | Are item-slot HUD and actual inventory state always consistent? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A046 | Can the player switch between available items without losing state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A047 | Do weapon stow and deployment animations complete without conflicts? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A048 | Does weapon switching correctly interrupt or preserve in-progress actions according to established rules? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A049 | Are pickup effects, particles and destroyed crates cleaned up? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A050 | Can repeated pickup, use, depletion and replacement cycles run without state corruption? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A051 | Does the Machine Gun deploy and stow using its approved model and rack? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A052 | Do its barrels, articulation and firing animation operate correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A053 | Can it fire straight forward during normal driving without false self-obstruction? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A054 | Does aiming track and fire at accepted targets correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A055 | Are projectile origins and actual trajectories aligned? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A056 | Are the intended tracer colors, sequencing and visibility correct? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A057 | Do bullet impacts and hit effects correspond to actual collisions? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A058 | Do short bursts, sustained fire and start/stop transitions display consistently? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A059 | Does the weapon preserve target and driving visibility during heavy fire? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A060 | Where the approved Harlequin texture has been integrated, do geometry, drum magazines, material presentation and firing behavior remain correct? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A061 | Does the Missile deploy and stow correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A062 | Does the Missile launch from the correct physical origin? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A063 | Does normal-driving straight firing work without camera mode? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A064 | Does aim mode permit correct manual leading of moving targets? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A065 | Does the standard Missile remain non-homing after launch? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A066 | Are its projectile model, flight animation and VFX displayed correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A067 | Do terrain and collision interactions follow the approved behavior? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A068 | Are impact, explosion and damage effects consistent with collision outcomes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A069 | Do ammunition, cooldown and destruction rules remain correct? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A070 | Do repeated launches, switching and recovery avoid stuck projectiles or weapon state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A071 | Does the trunk, rack, turret and catapult deploy in the correct mechanical sequence? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A072 | Does the turret correctly follow accepted camera aiming? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A073 | Does the winch visibly cock the spoon and load the next projectile? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A074 | Are all five projectiles retained correctly during driving, jumps and inversion? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A075 | Does the first trigger initiate the intended automatic five-round volley? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A076 | Do subsequent shots maintain their intended firing interval without cancellation? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A077 | Does each projectile follow its own ballistic launch trajectory without after-launch steering? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A078 | Are the fuse, firework motion, sparks and smoke trail correctly presented? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A079 | Do explosions, blast radius, shield interactions and damage match approved behavior without duplicate damage? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A080 | Does repeated Salvo use avoid mechanical clipping, stuck loading, smoke accumulation and cleanup failures? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A081 | Does Jack-in-the-Box Oil deploy and launch using its approved presentation? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A082 | Do oil patches form at the intended terrain/contact locations? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A083 | Does wheel contact trigger the appropriate contamination state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A084 | Does contamination produce the established traction and handling changes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A085 | Are oil spray, trails and patch visuals consistent with gameplay state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A086 | Do oil effects expire or clean up correctly without leaving stale traction changes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A087 | Does the Proxy Mine deploy at its intended location and orientation? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A088 | Does mine proximity detection trigger according to its established rules? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A089 | Do mine explosion VFX and damage occur once and match the physical event? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A090 | Do multiple active oil patches and mines interact and clean up without runtime or collision problems? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A091 | Does the carried Shield preserve its approved protective geometry? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A092 | Does deployment produce the straight central face and backward-angled wings? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A093 | Does the Shield deploy upright and settle on flats, banks and slopes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A094 | Does the deployed Shield behave as a heavy but movable physical object? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A095 | Do vehicle impacts produce established collision behavior and damage without duplication? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A096 | Does Shield durability and destruction remain correct in carried and deployed states? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A097 | Are Machine Gun and Missile hits intercepted correctly where Shield geometry blocks them? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A098 | Does a Shield correctly deny an EMP Claw impact against a protected vehicle? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A099 | Do blocked rear explosions and exposed front or partial-coverage explosions follow directional protection rules? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A100 | Do multiple Shields and repeated deployment/destruction avoid stale protection, collision state and performance problems? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A101 | Does the EMP Claw launcher deploy and present correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A102 | Does the Claw guide and navigate around terrain according to established behavior? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A103 | Does vehicle attachment occur correctly and apply the intended EMP debuff? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A104 | Do claw noses, stowed ammunition, electricity VFX and counterplay behave correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A105 | Do EMP status, HUD and DevTools integration remain consistent with actual state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A106 | Does the Wrench deploy its robotic repair arm without unwanted clipping? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A107 | Does the Wrench complete its intended three-point hood repair sequence? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A108 | Does repair restore the correct health and update displayed status? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A109 | Does Boost apply the established acceleration or movement behavior? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A110 | Do Boost exhaust, camera sensation and visuals remain coherent without introducing handling or performance regressions? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A111 | Are current weapon models, mounts and materials displayed correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A112 | Are visible projectiles and fired effects aligned with their physical origins? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A113 | Are explosions and impacts readable without masking important gameplay? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A114 | Do deployed items remain visually consistent with their actual collision shapes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A115 | Are animations smooth when beginning, interrupting and completing actions? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A116 | Do arena meshes, props and terrain display without noticeable asset defects? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A117 | Do particle effects and visual effects stop at the correct lifecycle point? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A118 | Are implemented weapon, impact, vehicle and environmental sounds triggered correctly? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A119 | Are sound levels, spatial behavior and interruption consistent with established audio behavior? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A120 | Are important visual feedback states readable at normal gameplay distances and under sustained combat effects? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A121 | What FPS, frame-time and resource baseline can be measured under documented idle conditions? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A122 | How do driving and vehicle physics affect frame-time and CPU workload? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A123 | How does performance change with two to four cars when the test environment supports them? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A124 | Do Machine Gun firing, tracers and impact effects produce measurable spikes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A125 | Do Missile and Salvo projectiles, explosions and smoke produce measurable spikes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A126 | Do Shield physics, collision and multiple deployed Shields produce measurable spikes? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A127 | Do oil patches, mines, crates, confetti and world effects create persistent workload? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A128 | Do render workload, arena props, lights, meshes and materials show measurable bottlenecks? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A129 | Do memory, object counts and frame-time behavior remain stable during repeated-use and extended sessions? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A130 | Are performance results reproducible under controlled conditions, with environment and instrumentation limits explicitly recorded? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A131 | Can Astra launch and control two or more independent multiplayer instances? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A132 | Can clients join and maintain the same session without state divergence? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A133 | Are vehicle movement, orientation, collisions, damage and respawn synchronized? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A134 | Are crate pickup, item-slot changes, weapon switching and ammo consumption authoritative? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A135 | Do Machine Gun, Missile and Salvo firing, impacts and damage agree across host and clients? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A136 | Do oil contamination, mines, Boost and Wrench actions produce consistent remote state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A137 | Do Shield deployment, collision, durability, protection and destruction remain authoritative? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A138 | Do EMP Claw navigation, attachment, debuff and Shield interception agree across clients? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A139 | Can network delay, concurrent actions and disconnect/reconnect scenarios be exercised without duplication or stale state? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
| A140 | Can multi-client weapon interactions and sustained combat run without uncorrected divergence, runtime errors or escalating performance problems? | NOT TESTED | Not executed: pilot submission gate failed; [blocker](tally-submission-blocked.jpg). |
