struct TextGeometryInput
{
    min16float4 pos : SV_POSITION;
    min16float2 uv : TEXCOORD0;
    uint instId : TEXCOORD1;
};

struct TextGeometryOutput
{
    min16float4 pos : SV_POSITION;
    min16float2 uv : TEXCOORD0;
    uint rtvId : SV_RenderTargetArrayIndex;
};

[maxvertexcount(3)]
void main(triangle TextGeometryInput input[3], inout TriangleStream<TextGeometryOutput> outStream)
{
    TextGeometryOutput output;
    [unroll(3)]
    for (int i = 0; i < 3; ++i)
    {
        output.pos = input[i].pos;
        output.uv = input[i].uv;
        output.rtvId = input[i].instId;
        outStream.Append(output);
    }
}
