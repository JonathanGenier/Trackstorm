shader_type spatial;
render_mode cull_disabled;
uniform sampler2D surface_field : filter_linear, repeat_disable;

void fragment() {
    if (texture(surface_field, UV).a < 0.5) { discard; }
    ALBEDO = vec3(0.075, 0.22, 0.25);
    ROUGHNESS = 0.24;
    METALLIC = 0.15;
}
