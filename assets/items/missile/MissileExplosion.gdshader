shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back;

uniform float age = 0.0;
uniform float intensity = 1.0;
varying vec3 local_pos;

float hash(vec3 p) { return fract(sin(dot(p, vec3(12.9898, 78.233, 37.719))) * 43758.5453); }
float noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1,0,0)), f.x),
                   mix(hash(i + vec3(0,1,0)), hash(i + vec3(1,1,0)), f.x), f.y),
               mix(mix(hash(i + vec3(0,0,1)), hash(i + vec3(1,0,1)), f.x),
                   mix(hash(i + vec3(0,1,1)), hash(i + vec3(1,1,1)), f.x), f.y), f.z);
}
void vertex() {
    local_pos = VERTEX;
    VERTEX *= 0.86 + 0.14 * sin(VERTEX.x * 7.0 + VERTEX.z * 5.0 + age * 9.0);
}
void fragment() {
    float n = noise(local_pos * 7.0 + vec3(0.0, -age * 3.0, 0.0));
    n = n * 0.7 + noise(local_pos * 17.0 - age * 5.0) * 0.3;
    float heat = smoothstep(0.25, 0.82, n);
    vec3 flame = mix(vec3(0.85, 0.035, 0.003), vec3(1.0, 0.48, 0.025), heat);
    flame = mix(flame, vec3(1.0, 0.88, 0.5), smoothstep(0.65, 0.9, n) * (1.0 - age));
    float rim = pow(max(dot(normalize(NORMAL), normalize(VIEW)), 0.0), 0.7);
    float fade = (1.0 - smoothstep(0.2, 1.0, age));
    ALBEDO = flame * (0.7 + intensity * 0.3);
    EMISSION = flame * intensity * 0.3;
    ALPHA = rim * fade * smoothstep(age * 0.75, age * 0.75 + 0.2, n) * 0.78;
}
