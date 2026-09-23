extends SceneTree
var loaded: Resource
var completed_frames = 0
func _initialize():
    ResourceLoader.load_threaded_request("res://scenes/maps/oval_foundation.tscn", "", "--subthreads" in OS.get_cmdline_user_args())
func _process(_delta):
    if loaded:
        completed_frames += 1
        if completed_frames == 20:
            quit()
    elif ResourceLoader.load_threaded_get_status("res://scenes/maps/oval_foundation.tscn") == ResourceLoader.THREAD_LOAD_LOADED:
        loaded = ResourceLoader.load_threaded_get("res://scenes/maps/oval_foundation.tscn")
        print("MAP LOADED")
    return false
