extends SceneTree
func _initialize() -> void:
	call_deferred("run")
func run() -> void:
	var main = load("res://scenes/main.tscn").instantiate()
	main.set("StartupEnabled",false)
	main.set("OnlineEnabled",false)
	root.add_child(main)
	for i in 180: await process_frame
	var timings: Array[float] = []
	var previous := Time.get_ticks_usec()
	for i in 600:
		await process_frame
		var now := Time.get_ticks_usec()
		timings.append((now-previous)/1000.0)
		previous=now
	timings.sort()
	var sum := 0.0
	for value in timings: sum+=value
	print("TS100 idle practice 8 vehicles, 600 rendered frames: mean_ms=",sum/timings.size()," p95_ms=",timings[570]," max_ms=",timings[-1]," draw_calls=",Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME)," objects=",Performance.get_monitor(Performance.RENDER_TOTAL_OBJECTS_IN_FRAME))
	await RenderingServer.frame_post_draw
	root.get_texture().get_image().save_png("res://.godot/ts100-practice-final.png")
	main.queue_free()
	for i in 60: await process_frame
	quit()
