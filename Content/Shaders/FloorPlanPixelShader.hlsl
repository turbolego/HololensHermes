// FloorPlanPixelShader.hlsl
// Samples the floor plan PNG texture and renders it slightly translucent
// so the real-world floor remains partly visible through the overlay.
// Uses the same texture/sampler register convention as the text pipeline.

Texture2D floorPlanTexture : register(t0);
SamplerState floorPlanSampler : register(s0);

struct FloorPlanPixelInput
{
    min16float4 pos : SV_POSITION;
    min16float2 uv  : TEXCOORD0;
};

min16float4 main(FloorPlanPixelInput input) : SV_TARGET
{
    min16float4 c = floorPlanTexture.Sample(floorPlanSampler, input.uv);
    // Keep the floor plan readable but see-through.
    return min16float4(c.rgb, c.a * 0.8f);
}
