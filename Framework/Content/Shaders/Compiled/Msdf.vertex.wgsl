struct type_u002e_VertexUniformBlock {
    Matrix: mat4x4<f32>,
}

struct VertexOutput {
    @builtin(position) member: vec4<f32>,
    @location(0) member_1: vec2<f32>,
    @location(1) member_2: vec4<f32>,
}

@group(1) @binding(0) 
var<uniform> VertexUniformBlock: type_u002e_VertexUniformBlock;
var<private> in_u002e_var_u002e_TEXCOORD0_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD1_1: vec2<f32>;
var<private> in_u002e_var_u002e_TEXCOORD2_1: vec4<f32>;
var<private> global: vec4<f32> = vec4<f32>(0f, 0f, 0f, 1f);
var<private> out_u002e_var_u002e_TEXCOORD0_: vec2<f32>;
var<private> out_u002e_var_u002e_TEXCOORD1_: vec4<f32>;

fn vertex_main_1() {
    let _e10 = in_u002e_var_u002e_TEXCOORD0_1;
    let _e11 = in_u002e_var_u002e_TEXCOORD1_1;
    let _e12 = in_u002e_var_u002e_TEXCOORD2_1;
    let _e14 = VertexUniformBlock.Matrix;
    global = (vec4<f32>(_e10.x, _e10.y, 0f, 1f) * transpose(_e14));
    out_u002e_var_u002e_TEXCOORD0_ = _e11;
    out_u002e_var_u002e_TEXCOORD1_ = _e12;
    return;
}

@vertex 
fn vertex_main(@location(0) in_u002e_var_u002e_TEXCOORD0_: vec2<f32>, @location(1) in_u002e_var_u002e_TEXCOORD1_: vec2<f32>, @location(2) in_u002e_var_u002e_TEXCOORD2_: vec4<f32>) -> VertexOutput {
    in_u002e_var_u002e_TEXCOORD0_1 = in_u002e_var_u002e_TEXCOORD0_;
    in_u002e_var_u002e_TEXCOORD1_1 = in_u002e_var_u002e_TEXCOORD1_;
    in_u002e_var_u002e_TEXCOORD2_1 = in_u002e_var_u002e_TEXCOORD2_;
    vertex_main_1();
    let _e10 = global.y;
    global.y = -(_e10);
    let _e12 = global;
    let _e13 = out_u002e_var_u002e_TEXCOORD0_;
    let _e14 = out_u002e_var_u002e_TEXCOORD1_;
    return VertexOutput(_e12, _e13, _e14);
}
