extends SceneTree
func _initialize():
    call_deferred("capture")
func capture():
    for name in ["OldMap", "NewMap"]:
        var view = SubViewport.new()
        view.size = Vector2i(768, 288)
        view.own_world_3d = true
        view.render_target_update_mode = SubViewport.UPDATE_ALWAYS
        root.add_child(view)
        var map = load("res://scenes/arena/prototype_arena.tscn" if name == "OldMap" else "res://scenes/maps/oval_foundation.tscn").instantiate()
        view.add_child(map)
        var env = WorldEnvironment.new()
        env.environment = Environment.new()
        env.environment.background_mode = Environment.BG_COLOR
        env.environment.background_color = Color("8098a5")
        env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
        env.environment.ambient_light_color = Color("becad1")
        env.environment.ambient_light_energy = 0.65
        view.add_child(env)
        var sun = DirectionalLight3D.new()
        sun.rotation_degrees = Vector3(-48, -30, 0)
        sun.light_color = Color("ffe3bd")
        sun.shadow_enabled = true
        view.add_child(sun)
        var cam = Camera3D.new()
        cam.position = Vector3(60, 58, 74) if name == "OldMap" else Vector3(180, 190, 230)
        cam.fov = 62
        cam.far = 3000
        view.add_child(cam)
        cam.look_at(Vector3.ZERO)
        cam.current = true
        for frame in range(8):
            await process_frame
        await RenderingServer.frame_post_draw
        view.get_texture().get_image().save_png("res://assets/frontend/lobby/" + name + ".png")
        view.queue_free()
        await process_frame
    quit()
