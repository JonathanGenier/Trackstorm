extends SceneTree
func _initialize():
    call_deferred("capture")
func capture():
    var world = Node3D.new()
    root.add_child(world)
    world.add_child(load("res://scenes/maps/oval_foundation.tscn").instantiate())
    var environment = WorldEnvironment.new()
    environment.environment = load("res://assets/maps/oval/Daylight.tres")
    world.add_child(environment)
    var sun = DirectionalLight3D.new()
    sun.rotation_degrees = Vector3(-55,-25,0)
    sun.light_energy = 1.4
    world.add_child(sun)
    var camera = Camera3D.new()
    camera.current = true
    camera.far = 1500
    world.add_child(camera)
    var data = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/oval/measurements.json"))
    var s = data.sections_godot[0]
    var rim = Vector3(s[1][0],s[1][1],s[1][2])
    var outward = Vector3(s[1][0]-s[0][0],0,s[1][2]-s[0][2]).normalized()
    var next = data.sections_godot[24][1]
    camera.position = rim-outward*5+Vector3.UP*2.5
    camera.look_at(Vector3(next[0],next[1]+3,next[2]))
    for i in 6: await process_frame
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png("res://.godot/boundary-checks/track-level.png")
    print("TS174 track-level reference capture complete.")
    quit()
