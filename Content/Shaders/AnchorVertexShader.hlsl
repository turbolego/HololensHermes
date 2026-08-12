// AnchorRenderer.hlsl
// Vertex shader for the world-locked target marker + arrow.
// Uses ModelConstantBuffer (b0) for the marker's world transform and
// ViewProjectionConstantBuffer (b1) from CameraResources.
// Instance ID is used to select left/right view for stereo rendering.

#include "VertexShaderShared.hlsl"

struct AnchorVertexInput
{
    min16float3 pos   : POSITION;
    min16float3 color : COLOR0;
    min16float2 uv    : TEXCOORD0;
};

struct AnchorVertexOutput
{
    min16float4 pos   : SV_POSITION;
    min16float3 color : COLOR0;
    min16float2 uv    : TEXCOORD0;
    uint        viewId : TEXCOORD1;
};

AnchorVertexOutput main(AnchorVertexInput input)
{
    AnchorVertexOutput output;
    float4 p = float4(input.pos, 1.0f);
    int idx = input.instId % 2;
    p = mul(p, model);
    p = mul(p, viewProjection[idx]);
    output.pos   = (min16float4)p;
    output.color = input.color;
    output.uv    = input.uv;
    output.viewId = idx;
    return output;
}
