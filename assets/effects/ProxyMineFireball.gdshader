shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

uniform float age = 0.0;
uniform float strength = 0.0;
uniform float layer = 0.0;
uniform float phase = 0.0;

varying vec3 local_point;

float hash(vec3 p) {
    p = fract(p * 0.3183099 + vec3(0.13, 0.27, 0.41));
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
        mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
    float billow = noise(VERTEX * 2.8 + vec3(phase, -age * 3.4, 0.0));
    float swell = 0.82 + billow * 0.38 + sin(VERTEX.y * 9.0 + TIME * 18.0 + phase) * 0.06;
    VERTEX *= swell;
    VERTEX.y += sin(VERTEX.x * 7.0 + TIME * 14.0 + phase) * 0.09;
    local_point = VERTEX;
}

void fragment() {
    vec3 p = normalize(local_point) * (3.7 + layer * 1.9);
    p += vec3(0.0, -age * (3.7 + layer), 0.0);
    float coarse = noise(p * 1.4 + phase);
    float detail = noise(p * 3.0 - vec3(0.0, age * 2.3, 0.0));
    float turbulence = coarse * 0.72 + detail * 0.28;
    float tongues = smoothstep(0.28, 0.68, turbulence);
    float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.25);
    float hot = smoothstep(0.38, 0.70, turbulence);
    vec3 ember = mix(vec3(0.54, 0.055, 0.008), vec3(1.0, 0.29, 0.025), tongues);
    vec3 flame = mix(ember, vec3(1.0, 0.81, 0.31), hot * (1.0 - layer * 0.32));
    ALBEDO = flame;
    EMISSION = flame * (1.8 - layer * 0.55);
    float density = mix(0.72 + tongues * 0.28, smoothstep(0.27, 0.61, turbulence), layer);
    ALPHA = strength * mix(0.92, 0.74, layer) * density * (1.0 - rim * 0.28);
}
