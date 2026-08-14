// Constant buffers shared by all stereo holographic vertex shaders.
// This file intentionally has no entry point or vertex input/output types, so
// specialized shaders can include it without introducing an additional main.

cbuffer ModelConstantBuffer : register(b0)
{
    float4x4 model;
};

cbuffer ViewProjectionConstantBuffer : register(b1)
{
    float4x4 viewProjection[2];
};
