using System.Numerics;

namespace HololensHermes.Content
{
    /// <summary>
    /// Constant buffer used to send hologram position transform to the shader pipeline.
    /// </summary>
    internal struct ModelConstantBuffer
    {
        public Matrix4x4 model;
    }

    /// <summary>
    /// Used to send per-vertex data to the vertex shader.
    /// </summary>
    internal struct VertexPositionColor
    {
        public VertexPositionColor(Vector3 pos, Vector3 color)
        {
            this.pos   = pos;
            this.color = color;
        }

        public Vector3 pos;
        public Vector3 color;
    };

    /// <summary>
    /// Per-vertex data for textured geometry (floor plan quad).
    /// Layout matches FloorPlanVertexShader.hlsl: POSITION (R32G32B32_Float) +
    /// TEXCOORD0 (R32G32_Float). SV_InstanceID is provided by the hardware.
    /// </summary>
    internal struct VertexPositionTexture
    {
        public VertexPositionTexture(Vector3 pos, Vector2 uv)
        {
            this.pos = pos;
            this.uv  = uv;
        }

        public Vector3 pos;
        public Vector2 uv;
    }
}
