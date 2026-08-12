Texture2D glyphAtlas : register(t0);
SamplerState glyphSampler : register(s0);

struct TextPixelInput
{
    min16float4 pos : SV_POSITION;
    min16float2 uv : TEXCOORD0;
};

min16float4 main(TextPixelInput input) : SV_TARGET
{
    min16float4 s = glyphAtlas.Sample(glyphSampler, input.uv);
    if (s.a < 0.1f)
    {
        discard;
    }

    return min16float4(1.0f, 0.75f, 0.2f, s.a);
}
