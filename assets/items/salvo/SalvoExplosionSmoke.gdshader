shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, skip_vertex_transform;

uniform sampler2D smoke_texture : source_color, filter_linear_mipmap;
uniform float age = 0.0;
varying float seed;

void vertex() {
    seed = INSTANCE_CUSTOM.r;
    vec3 center = MODEL_MATRIX[3].xyz;
    float growth = 0.35 + 0.65 * smoothstep(0.0, 0.7, age) + min(age, 20.0) * 0.045;
    center += vec3(sin(seed * 31.0) * age * 0.015, age * (0.04 + seed * 0.025), cos(seed * 29.0) * age * 0.015);
    float size = (1.15 + INSTANCE_CUSTOM.g * 0.7) * growth;
    vec3 world = center + (INV_VIEW_MATRIX[0].xyz * VERTEX.x + INV_VIEW_MATRIX[1].xyz * VERTEX.y) * size;
    VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
}

void fragment() {
    vec2 uv = UV;
    if (seed > 0.5) { uv.x = 1.0 - uv.x; }
    vec4 smoke = texture(smoke_texture, uv);
    float fade = 1.0 - smoothstep(1.5, 20.0, age);
    float flash = max(0.0, 1.0 - mod(age, 0.16) / 0.12) * (1.0 - step(0.44, age));
    vec3 tint = mix(vec3(0.9, 0.12, 0.25), vec3(0.12, 0.65, 0.9), seed);
    ALBEDO = vec3(0.07, 0.065, 0.08) + smoke.rgb * 0.11 + tint * flash * 0.3;
    ALPHA = smoke.a * 0.62 * fade;
}
