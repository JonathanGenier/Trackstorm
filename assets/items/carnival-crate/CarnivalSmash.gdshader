shader_type spatial;
render_mode world_vertex_coords, cull_disabled;

uniform float age = 0.0;
uniform float retire_opacity = 1.0;
uniform int kind = 0;
uniform mat4 burst_transform;
uniform mat4 burst_inverse;
uniform float effect_scale = 1.0;
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
    vec3 axis = normalize(mat3(burst_transform)*vec3(sin(seed*35.0), .35, cos(seed*21.0)));
    vec3 start = (burst_inverse*vec4(origin,1.0)).xyz;
    // A short, fast impulse opens the stack; it never becomes a room-sized blast.
    vec3 move = direction*vec3(.5+energy*.2,0.0,.3+energy*.12)*(1.0-exp(-7.0*t));
    if (kind == 1) {
        move.y = (1.3+energy)*t*exp(-3.0*t) - .35*t;
        move += vec3(sin(t*5.0+seed*20.0),0.0,cos(t*4.0+seed*20.0))*.06;
    } else {
        move.y = (2.2+energy*.7)*t - 4.8*t*t;
    }
    vec3 centre_local = start+move;
    centre_local.x = clamp(centre_local.x,-1.55,1.55);
    centre_local.z = clamp(centre_local.z,-.72,.72);
    centre_local = vec3(0.0,1.4,0.0)+(centre_local-vec3(0.0,1.4,0.0))*effect_scale;
    vec3 centre = (burst_transform*vec4(centre_local,1.0)).xyz;
    float depth = dot(centre-floor_point, floor_normal);
    if (depth < .07*effect_scale) {
        centre += floor_normal * (.07*effect_scale-depth);
    }
    float landing = kind == 1 ? 0.0 : (1.0-smoothstep(.08,.5,depth))*smoothstep(.2,.65,t);
    vec3 face_normal = normalize(MODEL_MATRIX[2].xyz);
    vec3 rest_axis = normalize(cross(face_normal,floor_normal)+vec3(.00001,0.0,.00001));
    float rest_angle = acos(clamp(dot(face_normal,floor_normal),-1.0,1.0))*landing;
    float turn = t*(4.0+seed*8.0)*(1.0-landing);
    VERTEX = centre + spin(spin(local,axis,turn),rest_axis,rest_angle)*effect_scale;
    NORMAL = normalize(spin(spin(NORMAL,axis,turn),rest_axis,rest_angle));
    opacity = (1.0-smoothstep(life*.35, life, age)) * retire_opacity;
}

void fragment() {
    if (kind == 0) {
        vec3 red = texture(red_wood,UV).rgb;
        vec3 ivory = texture(ivory_wood,UV).rgb;
        float stripe = seed < .17 ? 1.0 : (seed > .7 ? smoothstep(.27,.31,UV.x)*(1.0-smoothstep(.56,.60,UV.x)) : 0.0);
        vec3 painted = mix(red,ivory,stripe);
        // Original painted faces, bare endgrain and bevels keep the material tied to the crate.
        ALBEDO = mix(texture(raw_wood,UV).rgb,painted,paint);
        ROUGHNESS = mix(.84,.56,paint);
        METALLIC = 0.0;
        EMISSION = vec3(0.0);
    } else {
        vec3 gold = vec3(1.0, .64, .09);
        float glint = pow(max(0.0, sin(age*(9.0+seed*7.0)+seed*53.0)), 14.0);
        ALBEDO = gold;
        METALLIC = .86;
        ROUGHNESS = .22;
        EMISSION = gold*(.6+glint*.9) + vec3(1.0,.92,.58)*glint*.65;
    }
    ALPHA = opacity;
}
