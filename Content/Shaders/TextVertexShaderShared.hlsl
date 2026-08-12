cbuffer ModelConstantBuffer : register(b0)
{
    float4x4 model;
};

cbuffer ViewProjectionConstantBuffer : register(b1)
{
    float4x4 viewProjection[2];
};

struct TextVertexShaderInput
{
    min16float3 pos : POSITION;
    min16float2 uv : TEXCOORD0;
    uint instId : SV_InstanceID;
};

struct TextVertexShaderOutput
{
    min16float4 pos : SV_POSITION;
    min16float2 uv : TEXCOORD0;
    uint viewId : TEXCOORD1;
};

TextVertexShaderOutput main(TextVertexShaderInput input)
{
    TextVertexShaderOutput output;
    float4 p = float4(input.pos, 1.0f);
    int idx = input.instId % 2;
    p = mul(p, model);
    p = mul(p, viewProjection[idx]);
    output.pos = (min16float4)p;
    output.uv = input.uv;
    output.viewId = idx;
    return output;
}
