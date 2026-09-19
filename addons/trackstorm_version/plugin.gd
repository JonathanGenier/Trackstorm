@tool
extends EditorPlugin
class_name TrackstormVersionPlugin

const SOURCE = "res://Directory.Build.props"
var exporter: EditorExportPlugin

static func canonical_version() -> String:
    var parser := XMLParser.new()
    assert(parser.open(SOURCE) == OK, "Cannot read canonical Trackstorm version")
    var values: Array[String] = []
    while parser.read() == OK:
        if parser.get_node_type() == XMLParser.NODE_ELEMENT and parser.get_node_name() == "TrackstormVersion":
            assert(parser.read() == OK and parser.get_node_type() == XMLParser.NODE_TEXT)
            values.append(parser.get_node_data())
    assert(values.size() == 1, "Expected exactly one TrackstormVersion")
    var pattern := RegEx.new()
    pattern.compile("\\A0\\.(0|[1-9][0-9]{0,4})\\.(0|[1-9][0-9]{0,4})\\z")
    assert(pattern.search(values[0]) != null, "Invalid MAJOR.RELEASE.PR")
    var parts := values[0].split(".")
    assert(int(parts[1]) <= 65534 and int(parts[2]) <= 65534)
    return values[0]

func _enter_tree() -> void:
    ProjectSettings.set_setting("application/config/version", TrackstormVersionPlugin.canonical_version())
    exporter = VersionExport.new()
    add_export_plugin(exporter)

func _exit_tree() -> void:
    remove_export_plugin(exporter)

class VersionExport extends EditorExportPlugin:
    func _get_name() -> String:
        return "TrackstormCanonicalVersion"

    func _supports_platform(_platform: EditorExportPlatform) -> bool:
        return true

    func _should_update_export_options(_platform: EditorExportPlatform) -> bool:
        return true

    func _get_export_options_overrides(platform: EditorExportPlatform) -> Dictionary:
        if platform.get_os_name() == "Windows":
            var version := TrackstormVersionPlugin.canonical_version()
            # Windows numeric resources have four slots; the last slot is always zero.
            return {"application/file_version": version + ".0", "application/product_version": version + ".0"}
        return {}

    func _export_begin(_features: PackedStringArray, _debug: bool, _path: String, _flags: int) -> void:
        ProjectSettings.set_setting("application/config/version", TrackstormVersionPlugin.canonical_version())
