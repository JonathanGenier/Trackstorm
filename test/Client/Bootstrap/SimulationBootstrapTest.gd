class_name SimulationBootstrapTest
extends GdUnitTestSuite


class FixedUpdateObserver:
	extends Node

	signal completed

	func _physics_process(_delta: float) -> void:
		completed.emit()


func test_main_scene_composes_lobby_without_advancing_gameplay() -> void:
	var packed_scene := load("res://scenes/main.tscn") as PackedScene
	assert_object(packed_scene).is_not_null()
	var main_scene := auto_free(packed_scene.instantiate()) as Node
	add_child(main_scene)
	assert_str(main_scene.get_script().resource_path).is_equal(
		"res://code/Client/Bootstrap/SimulationBootstrap.cs"
	)
	assert_object(main_scene.get_node_or_null("PlayerInput")).is_not_null()
	assert_int(Engine.physics_ticks_per_second).is_equal(60)
	assert_object(main_scene.get_node_or_null("DevelopmentSession")).is_not_null()
	assert_int(main_scene.get("CurrentSimulationTick")).is_equal(0)

	var observer := auto_free(FixedUpdateObserver.new()) as FixedUpdateObserver
	observer.process_physics_priority = 100
	add_child(observer)

	await observer.completed
	assert_int(main_scene.get("CurrentSimulationTick")).is_equal(0)
	await observer.completed
	assert_int(main_scene.get("CurrentSimulationTick")).is_equal(0)
