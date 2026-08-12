// FloorPlanVertexShader.hlsl
// Textured quad vertex shader for the 2D floor plan overlay.
// Reuses the shared ModelConstantBuffer (b0) + ViewProjectionConstantBuffer (b1)
// convention used by the existing holographic scaffold.

#include "VertexShaderShared.hlsl"

struct FloorPlanVertexInput
{
    min16float3 pos   : POSITION;
    min16float2 uv    : TEXCOORD0;
};

struct FloorPlanVertexOutput
{
    min16float4 pos   : SV_POSITION;
    min16float2 uv    : TEXCOORD0;
    uint        viewId : TEXCOORD1;
};

FloorPlanVertexOutput main(FloorPlanVertexInput input)
{
    FloorPlanVertexOutput output;
    float4 p = float4(input.pos, 1.0f);
    int idx = input.instId % 2;
    p = mul(p, model);
    p = mul(p, viewProjection[idx]);
    output.pos  = (min16float4)p;
    output.uv   = input.uv;
    output.viewId = idx;
    return output;
}
