function Get-FastCheckPlan {
    param(
        [Parameter(Mandatory)]
        [string[]]$Paths
    )

    $normalized = @($Paths | ForEach-Object { $_.Replace('\', '/') })

    $coreTests = $false
    $transportTests = $false
    $serviceTests = $false
    $clientBuild = $false
    $versionChecks = $false
    $mediaChecks = $false
    $runtime = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $extended = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $manual = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    function Add-Runtime([string]$Script) {
        [void]$runtime.Add($Script)
    }

    function Add-Extended([string]$Script) {
        [void]$extended.Add($Script)
    }

    function Add-Manual([string]$Scenario) {
        [void]$manual.Add($Scenario)
    }

    $hasProductionChanges = [bool]($normalized | Where-Object {
        $_ -match '^code/' -or
        $_ -match '^test/' -or
        $_ -match '^services/' -or
        $_ -match '^scenes/' -or
        $_ -match '^assets/' -or
        $_ -eq 'project.godot' -or
        $_ -match '\.(cs|csproj|gd|tscn|tres|blend|jsonc|js)$'
    })

    foreach ($path in $normalized) {
        if ($path -match 'Missile|missile_terrain|check-missile-terrain') {
            Add-Runtime 'check-missile-terrain.ps1'
            Add-Manual 'Observe heavy missile pitch response, banks/ramps, cliff departure, reacquisition and repeated firing under latency with the rendered missile harness.'
        }
        if ($path -match 'CombatCollision|combat_collision|check-combat-collision|VehicleCollision|VehicleMovement.cs|VehicleAuthority.cs|VehicleDamageMath') {
            Add-Runtime 'check-combat-collisions.ps1'
            Add-Extended 'check-environment-collision-network.ps1'
            Add-Manual 'Observe momentum-biased damage, parked/off-center side hits, low-speed loaded pushing and hills; repeat under multiplayer latency.'
        }
        if ($path -match 'RepeatedVehicleContact|FollowingContactNetwork|repeated_vehicle_contact|following_contact_network|check-repeated-collisions|check-following-contact|NetworkVehicleBody|NetworkVehicleArena.cs|RemoteInterpolation|TerrainCollision|WheelSuspension|EnvironmentContact') {
            Add-Runtime 'check-repeated-collisions.ps1'
            Add-Extended 'check-following-contact-network.ps1'
            Add-Manual 'Observe slow/repeated rock pressure, landing, debris expiry and closely following vehicle contact through the rendered native adapters.'
        }
        if ($path -match 'RockCollision|rock_collision|check-rock-collision' -or $path -eq 'assets/environment/ImportAsset.gd') {
            Add-Runtime 'check-rock-collisions.ps1'
            Add-Extended 'check-rock-collision-network.ps1'
            Add-Manual 'Observe direct/glancing rock impacts, held contact, repeated re-contact and reverse separation through practice and multiplayer.'
        }
        if ($path -match 'CrashRecovery|VehicleCrash|TerrainCollision|crash_recovery|check-crash-recovery') {
            Add-Runtime 'check-crash-recovery.ps1'
            Add-Manual 'Observe nose, rear, roof, side, awkward and repeated tumbles, first-wheel control restoration, and extreme tire landings through both vehicle adapters.'
        }
        if ($path -match 'VehicleContactBatch|MixedContact|mixed_contact|check-mixed-contacts|VehicleBody.cs') {
            $transportTests = $true
            Add-Runtime 'check-mixed-contacts.ps1'
            Add-Runtime 'check-pit-collisions.ps1'
            Add-Runtime 'check-environment-collisions.ps1'
            Add-Runtime 'check-trophy-truck.ps1'
            Add-Runtime 'check-repeated-collisions.ps1'
            Add-Manual 'Observe simultaneous vehicle-plus-barrier and vehicle-plus-terrain contacts, including low-closing touching and preserved native slowdown/rotation.'
        }
        if ($path -match 'PitCollision|pit_collision|check-pit-collision|VehicleCollision|VehicleBody.cs|NetworkVehicleBody') {
            Add-Runtime 'check-pit-collisions.ps1'
        }
        if ($path -match 'TrophyTruck|trophy_truck|check-trophy-truck|VehicleCollision|NetworkVehicleBody') {
            Add-Runtime 'check-trophy-truck.ps1'
        }
        if ($path -match 'WeaponAim|Aiming|weapon_aim|check-weapon-aim') {
            Add-Runtime 'check-weapon-aim.ps1'
            Add-Manual 'Inspect aiming while driving, close/airborne/crossing targets, mouse and physical controller feel, and impaired remote articulation with check-weapon-aim.ps1 -Visual -Impaired.'
        }
        if ($path -match 'CarRack|RackItemVisual|car_rack|check-car-rack') {
            Add-Runtime 'check-car-rack.ps1'
            Add-Manual 'Inspect selected rack payloads, two-slot switching, sustained use and remote visibility.'
        }
        if ($path -match 'CarArticulation|car_articulation|check-car-articulation|CarDeployment|WheelPresentation|assets/vehicles/') {
            Add-Runtime 'check-car-articulation.ps1'
            Add-Manual 'Inspect production Car silhouette, suspension clearance, steering, tire spin and trunk/rack deployment through rendered driving.'
        }
        if ($path -match 'NativeVehicleReplicationTests|ObservedImpairmentGateway|check-network-soak') {
            Add-Extended 'check-network-soak.ps1'
        }
        if ($path -match 'PostMatchIntegrationChecks|check-post-match') {
            Add-Runtime 'check-post-match.ps1'
        }
        if ($path -match 'ArenaBoundary|OutOfBounds|BoundaryIntegration|BuildPerimeter|CatchFence|boundary_checks|check-boundary') {
            Add-Runtime 'check-boundary.ps1'
            Add-Extended 'check-death-respawn.ps1'
            Add-Manual 'Run check-death-respawn.ps1 -OutOfBounds -Impaired; observe normal impacts, inward catch fence and successful ballistic escapes with OOB feedback through respawn.'
        }

        if ($path -match 'MatchStart|RaceCountdown|match_start_checks|check-match-start' -or $path -eq 'code/TransportTests/VehicleNetworkDriverTests.MatchEntry.cs') {
            Add-Runtime 'check-match-start.ps1'
        }
        if ($path -match 'IntegratedDriving|integrated_driving|check-integrated-driving') {
            Add-Runtime 'check-integrated-driving.ps1'
        }
        if ($path -match 'AirControl|air_control|check-air-control' -or $path -eq 'code/Core/Vehicles/VehicleMovement.cs') {
            Add-Runtime 'check-air-control.ps1'
            Add-Extended 'check-network-vehicles.ps1'
            Add-Extended 'check-reconnect.ps1'
        }
        if ($path -match 'DestructibleEnvironment|EnvironmentAuthority|EnvironmentTuning|EnvironmentLayout|EnvironmentRockState|EnvironmentSnapshot|EnvironmentCodec|EnvironmentRecoveryFixture|destructible_environment|check-destructible-environment') {
            Add-Runtime 'check-integrated-driving.ps1'
            Add-Runtime 'check-destructible-environment.ps1'
            Add-Extended 'check-reconnect.ps1'
            Add-Extended 'check-migration.ps1'
            Add-Manual 'Drive production rocks, plants and both tabletop transitions; inspect smallest-rock stability and repeated destruction with recovery.'
        }
        if ($path -match 'Salvo|salvo_checks|check-salvo') {
            Add-Runtime 'check-salvo.ps1'
            Add-Manual 'Play Salvo from the chase camera; inspect terrain-conforming marker privacy on remote peers and repeated use. Run impaired salvo, reconnect and migration checks.'
        }
        if ($path -match 'TireFeedback|TireMarkBatch|TireEffect|TireTrack|WaterWake|EnvironmentPresentation|EnvironmentPreset|EnvironmentSky|TerrainEffects|terrain_effects|check-terrain-effects') {
            Add-Runtime 'check-integrated-driving.ps1'
            Add-Runtime 'check-terrain-effects.ps1'
            Add-Runtime 'check-developer-options.ps1'
            Add-Manual 'Inspect all five environment packages at driving height; drive every surface, repeat tire effects and check Water splashes without persistent marks. Exercise environment replication and recovery.'
        }
        if ($path -match 'BuildDressing|infield_dressing|dressing_checks|check-dressing') {
            Add-Runtime 'check-dressing.ps1'
            Add-Runtime 'check-infield.ps1'
            Add-Runtime 'check-oval.ps1'
            Add-Manual 'Inspect dressed production map at driving height; drive routes, shoulders, jumps and two-car tunnel clearance.'
        }
        if ($path -match 'Water|water_checks|check-water' -or $path -eq 'docs/features/water.md') {
            Add-Runtime 'check-water.ps1'
            Add-Manual 'Drive repeated shallow/deep Water entry/exit and run check-death-respawn.ps1 -Water -Impaired for eight-peer lifecycle replication.'
        }
        if ($path -match '(?i)SurfaceIdentity|SurfaceField|BuildSurfaces|surface_checks|check-surfaces|surface_field|surface-sources' -or $path -eq 'docs/features/surfaces.md') {
            Add-Runtime 'check-surfaces.ps1'
            Add-Manual 'Inspect material readability, gradual shoulders, designated Water basin and local Stats during driving.'
        }
        # Feature-document-only edits stay cheap. Feature docs act as route hints only
        # when the same change also contains production/runtime files.
        if ($path -match '^docs/features/' -and -not $hasProductionChanges) {
            continue
        }

        if ($path -match '^assets/environment/' -or $path -match 'environment_library_checks|check-environment' -or $path -eq 'docs/features/environment-library.md') {
            Add-Runtime 'check-environment.ps1'
            Add-Manual 'Render check-environment.ps1 -Visual and inspect asset scale, materials, modular reuse and distance transitions.'
        }

        if ($path -match '^code/Core/' -or $path -match '^code/Tests/') {
            $coreTests = $true
        }

        if ($path -match '^code/Client/' -or
            $path -match '^test/Client/' -or
            $path -match '^scenes/' -or
            $path -match '^assets/' -or
            $path -eq 'project.godot' -or
            $path -eq 'Trackstorm.Client.csproj') {
            $clientBuild = $true
        }

        if ($path -match '^services/authority-lease/') {
            $serviceTests = $true
        }

        if ($path -match '^code/TransportTests/' -or
            $path -match '^code/(Core|Client)/Networking/' -or
            $path -match '^code/Client/Online/' -or
            $path -eq 'docs/features/transport.md' -or
            $path -eq 'docs/features/vehicle-networking.md' -or
            $path -eq 'docs/features/eos-p2p.md') {
            $transportTests = $true
        }

        if ($path -eq 'Directory.Build.props' -or
            $path -eq 'export_presets.cfg' -or
            $path -match '^tools/.*version.*\.ps1$' -or
            $path -eq '.github/version-transition.json') {
            $versionChecks = $true
        }

        if ($path -match '^assets/frontend/' -or $path -match '^tools/.*frontend-media.*\.ps1$') {
            $mediaChecks = $true
        }

        if ($path -match '^code/Client/Frontend/(HangingPlayMenu|PlayMenuSelection)' -or $path -match '^assets/frontend/play-menu/' -or $path -eq 'code/Client/Verification/PlayMenuChecks.cs') {
            Add-Runtime 'check-play-menu.ps1'
        }

        # Startup / application flow.
        if ($path -match '^code/Client/(Bootstrap|Assembly|Frontend)/' -or
            $path -match '^assets/frontend/main-menu/' -or
            $path -eq 'project.godot' -or
            $path -eq 'scenes/main.tscn' -or
            $path -eq 'docs/features/startup.md') {
            Add-Runtime 'check-startup.ps1'
        }

        if ($path -match 'BoostCamera|CameraSpeedStreaks|boost_camera|check-boost-camera' -or $path -eq 'code/Client/Vehicles/VehicleChaseCamera.cs') {
            Add-Runtime 'check-boost-camera.ps1'
            Add-Manual 'Inspect rendered Boost entry/sustain/exit, rapid reuse, steering/traffic, high speed without Boost and airborne camera readability with check-boost-camera.ps1 -Visual.'
        }

        # Vehicles, simulation and camera.
        if ($path -match 'TunnelScrape|tunnel_scrape|check-tunnel-scrape|VehicleMotionQuery' -or $path -eq 'code/Client/Networking/NetworkVehicleBody.cs') {
            Add-Runtime 'check-tunnel-scrape.ps1'
            Add-Manual 'Repeat the tunnel approach, airborne inner-bank fall and sustained scrape with camera shake disabled; inspect frame timing and recovery.'
        }
        if ($path -match 'WorldCollision|world_collision|check-world-collision' -or $path -match '^assets/maps/infield/(BuildTerrainCollision|BakeTerrainCollision|TerrainCollision|collision-audit|ImportTerrain)') {
            Add-Runtime 'check-world-collisions.ps1'
            Add-Extended 'check-world-collision-network.ps1'
            Add-Manual 'Playtest sustained steering into the tunnel dirt face, reverse/re-contact and inspect rendered bank, pillar, perimeter and rock impacts.'
        }
        if ($path -match '^code/(Core|Client)/Vehicles/' -or $path -match 'EnvironmentCollision|environment_collision|check-environment-collision' -or $path -eq 'code/Client/Networking/NetworkVehicleBody.cs') {
            Add-Runtime 'check-environment-collisions.ps1'
            Add-Extended 'check-environment-collision-network.ps1'
        }
        if ($path -match '^code/(Core|Client)/Vehicles/' -or $path -match 'TerrainHandling|terrain_handling|check-terrain-handling') {
            Add-Runtime 'check-terrain-handling.ps1'
            Add-Manual 'Drive all normal surfaces and transitions; restart uphill on infield grades; verify host tuning and client prediction.'
        }
        if ($path -match '^code/(Core|Client)/Vehicles/' -or
            $path -eq 'docs/features/vehicles.md' -or
            $path -eq 'scenes/verification/vehicle_checks.tscn') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Runtime 'check-trophy-truck.ps1'
            Add-Runtime 'check-landing.ps1'
            Add-Manual 'Drive/playtest the affected vehicle behavior, including multiple cars when collisions or shared physics are material.'
        }

        if ($path -match '^code/Core/Simulation/' -or $path -eq 'docs/features/simulation.md') {
            Add-Runtime 'check-vehicle.ps1'
            Add-Runtime 'check-landing.ps1'
            Add-Runtime 'check-match.ps1'
            Add-Manual 'Exercise sustained fixed-step gameplay and relevant multi-entity state transitions after simulation changes.'
        }

        if ($path -eq 'docs/features/camera.md' -or
            $path -match '^code/Client/.*/Camera' -or
            $path -match 'Camera.*\.cs$' -or
            $path -eq 'scenes/verification/camera_checks.tscn' -or
            $path -eq 'scenes/verification/camera_shake_playtest.tscn' -or
            $path -match 'camera_responsiveness|check-camera-responsiveness' -or
            $path -match '^scenes/verification/camera_obstruction_' -or
            $path -eq 'check-camera-obstruction.ps1' -or
            $path -eq 'check-camera-shake.ps1') {
            Add-Runtime 'check-camera.ps1'
            Add-Runtime 'check-camera-responsiveness.ps1'
            Add-Runtime 'check-camera-shake.ps1'
            Add-Runtime 'check-camera-obstruction.ps1'
            Add-Manual 'Playtest mouse/controller orbit and return, wall/terrain obstruction and clearing, camera framing, perceptible collision/damage shake and lifecycle transitions in practice and network gameplay. Run check-camera-obstruction.ps1 -Drive for native driving evidence.'
        }

        # Maps / arena.
        if ($path -match '^assets/(maps|environment)/' -or $path -match '^scenes/maps/' -or
            $path -match 'map_budget_checks|check-map-budget') {
            Add-Runtime 'check-map-budget.ps1'
        }
        if ($path -match '^code/(Core|Client)/Arenas/' -or
            $path -match '^scenes/arena/' -or
            $path -eq 'docs/features/arena.md') {
            Add-Runtime 'check-arena.ps1'
        }

        if ($path -match '^scenes/maps/' -or
            $path -eq 'docs/features/oval-map.md' -or
            $path -match '(?i)oval') {
            Add-Runtime 'check-oval.ps1'
            Add-Runtime 'check-infield.ps1'
            Add-Manual 'Drive the active map with representative multi-car traffic when map collision, scale, banking or spawn behavior changed.'
        }

        if ($path -match '(?i)infield') {
            Add-Runtime 'check-infield.ps1'
        }

        # Input, settings, menu and HUD.
        if ($path -match '^code/(Core|Client)/Input/' -or $path -eq 'docs/features/input.md') {
            Add-Runtime 'check-input.ps1'
        }

        if ($path -match '^code/(Core|Client)/Settings/' -or $path -eq 'docs/features/settings.md') {
            Add-Runtime 'check-settings.ps1'
        }

        if ($path -match '^code/Client/Hud/' -or $path -eq 'docs/features/hud.md') {
            Add-Runtime 'check-hud.ps1'
            Add-Manual 'Visually inspect affected HUD layout/readability at relevant resolutions.'
        }

        if ($path -eq 'docs/features/game-menu.md' -or $path -match '(?i)(MenuShell|GameMenu|EscapeMenu|MainMenu)') {
            Add-Runtime 'check-menu.ps1'
            Add-Manual 'Navigate the affected menu with mouse/keyboard/controller paths that are material to the change.'
        }

        if ($path -match '(?i)Shield|WorldWall|world.wall|ItemAuthority|NetworkVehicleBody|NetworkVehicleArena') { Add-Runtime 'check-world-wall.ps1'; Add-Manual 'Run check-world-wall.ps1 -ProductionMap -Impaired -Visual for production oval/infield deployment, then inspect upright sliding and yaw after vehicle impacts.' }
        if ($path -match '(?i)Shield') { Add-Runtime 'check-shield.ps1'; Add-Runtime 'check-shield-presentation.ps1' }
        if ($path -match '(?i)Shield|ItemAuthority|NetworkVehicleBody|NetworkVehicleArena') { Add-Runtime 'check-rear-shield.ps1' }
        if ($path -match '(?i)MachineGun|machine_gun|check-machine-gun') { Add-Runtime 'check-machine-gun.ps1' }

        if ($path -match '(?i)ProxyMine|proxy-mine|check-mine|mine_checks') { Add-Runtime 'check-mine.ps1' }

        if ($path -match '(?i)Nitro') { Add-Runtime 'check-nitro.ps1' }
        if ($path -match '(?i)BoostExhaust|boost_exhaust|check-boost-exhaust|BoostFlame|BoostThroat|assets/vehicles/boost/') {
            Add-Runtime 'check-boost-exhaust.ps1'
            Add-Runtime 'check-boost-camera.ps1'
            Add-Runtime 'check-nitro.ps1'
            Add-Manual 'Observe rear jet deployment, ignition, turbulent thrust, cutoff, residual smoke and multiple simultaneous Boost effects.'
        }

        # Items and lifecycle.
        if ($path -match '(?i)PickupDrive|MovingPickup|pickup_drive|check-pickup-drive|ItemSpawnAuthority|HostVehicleSession|NetworkVehicleArena|VehicleNetworkDriver') {
            Add-Runtime 'check-pickup-drive.ps1'
        }
        if ($path -match '^code/(Core|Client)/Items/' -or $path -eq 'docs/features/items.md') {
            Add-Runtime 'check-items.ps1'
            Add-Runtime 'check-oil.ps1'
            Add-Runtime 'check-nitro.ps1'
        }

        if ($path -eq 'docs/features/item-spawns.md' -or $path -match '(?i)(ItemSpawn|PickupSpawn|SpawnMarker)') {
            Add-Runtime 'check-item-spawns.ps1'
        }

        if ($path -eq 'docs/features/death-respawn.md' -or $path -match '(?i)(Death|Respawn)') {
            Add-Runtime 'check-death-respawn.ps1'
        }

        if ($path -match '^code/(Core/Matches|Tests/Matches)/Stunt' -or
            $path -match '(StuntIntegrationChecks|stunt_checks)' -or $path -eq 'docs/features/matches.md') {
            Add-Runtime 'check-stunts.ps1'
        }

        # Match lifecycle / standings.
        if ($path -match '^code/Core/Matches/' -or
            $path -eq 'docs/features/matches.md' -or
            $path -eq 'docs/features/game-loop.md') {
            Add-Runtime 'check-match.ps1'
        }

        if ($path -eq 'docs/features/standings.md' -or $path -match '(?i)Standing') {
            Add-Runtime 'check-standings.ps1'
        }

        # Sessions, lobby, reconnect and migration.
        if ($path -match '^code/Core/Sessions/' -or
            $path -eq 'docs/features/sessions.md' -or
            $path -eq 'docs/features/match-entry.md' -or
            $path -match '(?i)Lobby') {
            Add-Runtime 'check-lobby.ps1'
        }

        if ($path -eq 'docs/features/reconnection.md' -or $path -match '(?i)Reconnect') {
            Add-Runtime 'check-reconnect.ps1'
        }

        if ($path -eq 'docs/features/host-migration.md' -or $path -match '(?i)Migration') {
            Add-Runtime 'check-migration.ps1'
            Add-Extended 'check-migration-processes.ps1'
        }

        # Networking / online.
        if ($path -match '^code/(Core|Client)/Networking/' -or
            $path -eq 'docs/features/vehicle-networking.md') {
            Add-Runtime 'check-network-vehicles.ps1'
            Add-Manual 'Exercise multiple peers/cars under representative latency when synchronization, prediction or reconciliation changed.'
        }

        if ($path -match '^code/Client/Online/' -or $path -eq 'docs/features/eos-lobbies.md') {
            Add-Runtime 'check-online-lobby.ps1'
        }

        if ($path -eq 'docs/features/eos-identity.md' -or $path -match '(?i)EosIdentity|EosIntegrationChecks|check-eos\.ps1') {
            Add-Runtime 'check-eos.ps1'
            Add-Manual 'Run check-eos.ps1 -Authenticate / -P2p when development deployment access is available; native SDK initialization alone does not establish authenticated gameplay.'
        }

        if ($path -eq 'docs/features/eos-p2p.md' -or $path -match '(?i)EosP2p') {
            Add-Manual 'Run check-eos-multiplayer.ps1 with an exported executable and distinct authenticated devices when real EOS multiplayer verification is applicable.'
        }

        # DevTools.
        if ($path -eq 'docs/features/devtools.md' -or $path -match '(?i)DevTools') {
            Add-Runtime 'check-developer-options.ps1'
            Add-Runtime 'check-statistics.ps1'
            Add-Runtime 'check-event-log.ps1'
        }

        if ($path -eq 'docs/features/developer-options.md' -or $path -match '(?i)DeveloperOptions|ConfigurationRequest|ConfigurationChangesExport|ConfigurationChangesImport|LobbyNetworkDriver.Configuration|SharedConfiguration') {
            Add-Runtime 'check-developer-options.ps1'
        }

        if ($path -match '^code/Client/Statistics/' -or
            $path -eq 'docs/features/statistics.md' -or
            $path -match '(?i)Statistic') {
            Add-Runtime 'check-statistics.ps1'
        }

        if ($path -match '^code/Core/Events/' -or
            $path -eq 'docs/features/event-log.md' -or
            $path -match '(?i)EventLog') {
            Add-Runtime 'check-event-log.ps1'
        }

        if ($path -eq 'docs/features/activity-feed.md' -or $path -match '(?i)ActivityFeed') {
            Add-Runtime 'check-event-log.ps1'
            Add-Runtime 'check-hud.ps1'
        }

        # GDScript / GdUnit.
        if ($path -match '^test/Client/' -or $path -match '^addons/gdUnit4/') {
            Add-Runtime 'check-gdunit.ps1'
        }

        if ($path -eq 'import-godot.ps1' -or
            $path -eq 'check-gdunit.ps1' -or
            $path -eq 'check-gdunit-regression.ps1') {
            Add-Extended 'check-gdunit-regression.ps1'
        }

        # Audio.
        if ($path -match '^code/Client/Audio/' -or
            $path -eq 'docs/features/audio.md' -or
            $path -match '^assets/.*/(?i:audio|music|sound)/') {
            Add-Runtime 'check-audio.ps1'
            Add-Manual 'Listen to the affected audio in gameplay context for timing, balance, repetition and usefulness.'
        }
    }

    # Any C#/project change not already clearly covered still gets a Client compile safety net.
    if ($normalized | Where-Object { $_ -match '\.cs$' -or $_ -match '\.csproj$' -or $_ -match '\.props$' }) {
        $clientBuild = $true
    }

    [pscustomobject]@{
        CoreTests = $coreTests
        TransportTests = $transportTests
        ServiceTests = $serviceTests
        ClientBuild = $clientBuild
        VersionChecks = $versionChecks
        MediaChecks = $mediaChecks
        RuntimeScripts = @($runtime | Sort-Object)
        ExtendedScripts = @($extended | Sort-Object)
        ManualScenarios = @($manual | Sort-Object)
    }
}
