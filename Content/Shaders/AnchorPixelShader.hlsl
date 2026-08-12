// AnchorPixelShader.hlsl
// Picks either a solid color (no texture bound) or a textured marker.
// If a texture is bound to t0, sample it; otherwise fall back to the
// vertex color. This lets AnchorRenderer render a textured marker or a
// plain solid marker without changing the shader.

Texture2D anchorTexture : register(t0);
SamplerState anchorSampler : register(s0);

struct AnchorPixelInput
{
    min16float4 pos   : SV_POSITION;
    min16float3 color : COLOR0;
    min16float2 uv    : TEXCOORD0;
};

min16float4 main(AnchorPixelInput input) : SV_TARGET
{
    // If a texture is bound, sample it (marker icon / arrow).
    // Otherwise render the vertex color directly (debug / fallback).
    min16float4 texel = anchorTexture.Sample(anchorSampler, input.uv);
    if (texel.a < 0.01f)
    {
        discard;
    }
    return min16float4(texel.rgb, texel.a);
}
