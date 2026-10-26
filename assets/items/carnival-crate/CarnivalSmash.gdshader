shader_type spatial;
render_mode world_vertex_coords, cull_disabled;

uniform float age = 0.0;
uniform float retire_opacity = 1.0;
uniform int kind = 0;
uniform vec3 floor_point;
uniform vec3 floor_normal = vec3(0.0, 1.0, 0.0);
uniform sampler2D red_wood : source_color;
uniform sampler2D ivory_wood : source_color;
uniform sampler2D raw_wood : source_color;
varying float opacity;
varying float seed;
varying float paint;

vec3 spin(vec3 v, vec3 axis, float angle) {
    return v*cos(angle) + cross(axis,v)*sin(angle) + axis*dot(axis,v)*(1.0-cos(angle));
}

void vertex() {
    seed = INSTANCE_CUSTOM.z;
    paint = clamp(COLOR.r, 0.0, 1.0);
    COLOR = vec4(1.0);
    float life = INSTANCE_CUSTOM.w;
    float t = min(age, life);
    float angle = INSTANCE_CUSTOM.x * 6.2831853;
    float energy = INSTANCE_CUSTOM.y;
    vec3 direction = vec3(cos(angle), 0.0, sin(angle));
    vec3 origin = MODEL_MATRIX[3].xyz;
    vec3 local = VERTEX - origin;
    vec3 axis = normalize(vec3(sin(seed*35.0), .35, cos(seed*21.0)));
    float drag = kind == 0 ? .9 : 1.35;
    float speed = kind == 0 ? 4.0 + energy*5.0 : 3.0 + energy*4.0;
    vec3 move = direction * speed * (1.0-exp(-drag*t))/drag;
    if (kind == 1) {
        move.y = (4.0+energy*2.0)*t*exp(-.95*t) - .38*t;
        move += vec3(sin(t*4.0+seed*20.0),0.0,cos(t*3.0+seed*20.0))*.12*t;
    } else {
        move.y = (4.0+energy*4.0)*t - 4.9*t*t;
    }
    vec3 centre = origin + move;
    float depth = dot(centre-floor_point, floor_normal);
    if (depth < .10) {
        centre += floor_normal * (.10-depth + abs(sin(t*8.0+seed*20.0))*.18*exp(-t));
    }
    float turn_time = kind == 0 ? min(t, 1.8) : t;
    VERTEX = centre + spin(local, axis, turn_time*(3.0+seed*9.0));
    NORMAL = normalize(spin(NORMAL, axis, turn_time*(3.0+seed*9.0)));
    opacity = (1.0-smoothstep(life*.42, life, age)) * retire_opacity;
}

void fragment() {
    if (kind == 0) {
        vec3 painted = seed < .26 ? texture(ivory_wood, UV).rgb : texture(red_wood, UV).rgb;
        ALBEDO = mix(texture(raw_wood, UV).rgb, painted, paint);
        ROUGHNESS = .72;
        METALLIC = 0.0;
        EMISSION = vec3(0.0);
    } else {
        vec3 gold = vec3(1.0, .61, .075);
        float glint = pow(max(0.0, sin(age*(9.0+seed*7.0)+seed*53.0)), 14.0);
        ALBEDO = gold;
        METALLIC = .82;
        ROUGHNESS = .18;
        EMISSION = gold*(.65+glint*2.5) + vec3(1.0,.9,.5)*glint;
    }
    ALPHA = opacity;
}
