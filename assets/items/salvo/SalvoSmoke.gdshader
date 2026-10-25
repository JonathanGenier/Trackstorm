shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, skip_vertex_transform;

uniform sampler2D smoke_texture : source_color, filter_linear_mipmap;
uniform float smoke_clock = 0.0;
varying float age;
varying float seed;

void vertex() {
    age = max(0.0, smoke_clock - INSTANCE_CUSTOM.r);
    seed = INSTANCE_CUSTOM.g;
    vec3 center = MODEL_MATRIX[3].xyz;
    vec3 axis = MODEL_MATRIX[0].xyz;
    vec3 look = INV_VIEW_MATRIX[3].xyz - center;
    vec3 side = cross(normalize(axis), normalize(look));
    if (length(side) < 0.001) { side = INV_VIEW_MATRIX[0].xyz; }
    side = normalize(side);
    float growth = 1.0 + min(age, 15.0) * 0.13;
    center += vec3(sin(seed * 31.0) * age * 0.025, age * 0.10, cos(seed * 29.0) * age * 0.025);
    vec3 world = center + axis * VERTEX.x + side * VERTEX.y * INSTANCE_CUSTOM.b * growth;
    VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
}

void fragment() {
    if (age >= 15.0) { discard; }
    vec2 uv = UV;
    if (seed > 0.5) { uv.y = 1.0 - uv.y; }
    vec4 smoke = texture(smoke_texture, uv);
    float fade = 1.0 - smoothstep(2.0, 15.0, age);
    ALBEDO = vec3(0.025, 0.027, 0.030) + smoke.rgb * 0.035;
    ALPHA = smoke.a * 0.65 * fade;
}
