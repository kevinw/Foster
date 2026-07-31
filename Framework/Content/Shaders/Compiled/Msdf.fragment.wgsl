struct type_u002e_FragmentUniformBlock {
    DistanceRange: f32,
}

@group(3) @binding(0) 
var<uniform> FragmentUniformBlock: type_u002e_FragmentUniformBlock;
@group(2) @binding(0) 
var Texture: texture_2d<f32>;
@group(2) @binding(100) 
var Sampler: sampler;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec4<f32>;
var<private> out_u002e_var_u002e_SV_Target0_: vec4<f32>;

fn fragment_main_1() {
    let _e11 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e12 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e13 = textureSample(Texture, Sampler, _e11);
    let _e14 = textureDimensions(Texture, 0i);
    let _e20 = fwidth(_e11);
    let _e30 = FragmentUniformBlock.DistanceRange;
    out_u002e_var_u002e_SV_Target0_ = (_e12 * clamp(((max((0.5f * dot((vec2(_e30) / vec2<f32>(f32(_e14.x), f32(_e14.y))), (vec2<f32>(1f, 1f) / _e20))), 1f) * (max(min(_e13.x, _e13.y), min(max(_e13.x, _e13.y), _e13.z)) - 0.5f)) + 0.5f), 0f, 1f));
    return;
}

@fragment 
fn fragment_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec4<f32>) -> @location(0) vec4<f32> {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    fragment_main_1();
    let _e5 = out_u002e_var_u002e_SV_Target0_;
    return _e5;
}
