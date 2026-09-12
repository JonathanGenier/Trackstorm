class_name SimulationBootstrapTest
extends GdUnitTestSuite


func test_main_scene_composes_fixed_step_simulation() -> void:
	var packed_scene := load("res://scenes/main.tscn") as PackedScene
	assert_object(packed_scene).is_not_null()
	var main_scene := auto_free(packed_scene.instantiate()) as Node
	add_child(main_scene)
	await await_idle_frame()
	assert_str(main_scene.get_script().resource_path).is_equal(
		"res://code/Client/Bootstrap/SimulationBootstrap.cs"
	)
	assert_object(main_scene.get_node_or_null("PlayerInput")).is_not_null()
	assert_int(Engine.physics_ticks_per_second).is_equal(60)
