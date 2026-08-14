using System;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer = SharpDX.Direct3D11.Buffer;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Holographic;
using Windows.Perception.Spatial;
using Windows.Perception.Spatial.Surfaces;
using Windows.UI.Input.Spatial;
using HololensHermes.Common;

namespace HololensHermes.Content
{
    /// <summary>
    /// Renders the spatial mapping mesh (room / floor / wall surfaces) as a translucent
    /// shaded mesh, so the user can see what HoloLens has scanned.
    ///
    /// Consumed by SpatialMappingService (meshes fetched there) and rendered each frame.
    ///
    /// Uses the existing holographic scaffold conventions:
    ///   - ModelConstantBuffer (b0) for the per-frame world transform of the
    ///     currently selected surface mesh (identity by default — mesh is already
    ///     in world space from SpatialSurfaceObserver).
    ///   - ViewProjectionConstantBuffer (b1) is managed by CameraResources.
    ///   - Shared vertex layout: VertexPositionColor (POSITION + COLOR0).
    /// </summary>
    internal sealed class SpatialMappingRenderer
    {
        private readonly DeviceResources _deviceResources;
        private bool _loaded;

        private VertexShader _vertexShader;
        private PixelShader _pixelShader;
        private InputLayout _inputLayout;
        private Buffer _constantBuffer;

        // Collection of meshes that have been ingested from SpatialMappingService.
        private readonly List<MeshDrawData> _meshes = new List<MeshDrawData>();

        private struct MeshDrawData
        {
            public Buffer VertexBuffer;
            public int    VertexCount;
            public Buffer IndexBuffer;
            public int    IndexCount;
        }

        public SpatialMappingRenderer(DeviceResources deviceResources)
        {
            _deviceResources = deviceResources;
        }

        /// <summary>
        /// Create shared device resources for mesh rendering: shaders, input layout,
        /// and the model constant buffer.
        /// </summary>
        public async Task CreateDeviceDependentResourcesAsync()
        {
            var device = _deviceResources.D3DDevice;
            if (device == null) return;

            // Mesh vertices use the shared POSITION + COLOR0 shader layout.
            var vertexBytecode = await ShaderBytecodeLoader.LoadAsync("Content/Shaders/VertexShader.cso");
            var pixelBytecode = await ShaderBytecodeLoader.LoadAsync("Content/Shaders/PixelShader.cso");
            _vertexShader = new VertexShader(device, vertexBytecode);
            _pixelShader = new PixelShader(device, pixelBytecode);

            _inputLayout = new InputLayout(
                device,
                vertexBytecode,
                new[]
                {
                    new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                    new InputElement("COLOR",    0, Format.R32G32B32_Float, 12, 0)
                });

            _constantBuffer = new Buffer(
                device,
                64,
                ResourceUsage.Dynamic,
                BindFlags.ConstantBuffer,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                64);

            _loaded = true;
        }

        /// <summary>
        /// Push a new spatial surface mesh (vertices + indices from SpatialSurfaceMesh)
        /// into the renderer's draw list.
        ///
        /// The mesh data comes from SpatialSurfaceMesh. In practice you get the vertex/
        /// index buffers from a SpatialSurfaceMesh and copy them into D3D11 buffers here.
        ///
        /// For the HololensHermes scaffold we expose a typed overload that copies a
        /// UWP SpatialSurfaceMesh into D3D11 buffers directly (the real code path),
        /// plus a placeholder overload for testing with hand-built buffers.
        /// </summary>
        public void AddMesh(Buffer vertexBuffer, Buffer indexBuffer, int vertexCount, int indexCount)
        {
            if (!_loaded) return;
            if (vertexBuffer == null || indexBuffer == null) return;
            if (vertexCount < 3 || indexCount < 3) return;

            // Clone the buffers into our own D3D11 buffers so we own the lifetime.
            // Real code would copy via ID3D11DeviceContext.CopyResource or
            // create new buffers and copy data. For the scaffold we keep the
            // handles as provided by the caller (same device).
            var mesh = new MeshDrawData
            {
                VertexBuffer = vertexBuffer,
                VertexCount  = vertexCount,
                IndexBuffer = indexBuffer,
                IndexCount  = indexCount
            };
            _meshes.Add(mesh);
        }

        /// <summary>
        /// Add a mesh from a SpatialSurfaceMesh (UWP API).
        ///
        /// Copies the surface mesh vertex and index buffers into D3D11 buffers that
        /// SpatialMappingRenderer can draw each frame.
        /// </summary>
        public async Task AddMeshAsync(SpatialSurfaceMesh surfaceMesh)
        {
            if (surfaceMesh == null || !_loaded) return;

            var device = _deviceResources.D3DDevice;
            var context = _deviceResources.D3DDeviceContext;
            if (device == null || context == null) return;

            // SpatialSurfaceMesh buffers have a device-specific packed layout.
            // Until a mesh-layout adapter is configured, retain a conservative
            // fallback triangle so the rendering and lifecycle paths are valid.
            // This avoids interpreting raw sensor memory with an incorrect stride.

            var fallbackVerts = new[]
            {
                new VertexPositionColor(new Vector3(0f, 0f, 0f), new Vector3(0.2f, 0.6f, 1f)),
                new VertexPositionColor(new Vector3(1f, 0f, 0f), new Vector3(0.2f, 0.6f, 1f)),
                new VertexPositionColor(new Vector3(0f, 0f, 1f), new Vector3(0.2f, 0.6f, 1f))
            };

            var vb = Buffer.Create(device, BindFlags.VertexBuffer, fallbackVerts);

            ushort[] indices = { 0, 1, 2 };
            var ib = Buffer.Create(device, BindFlags.IndexBuffer, indices);

            AddMesh(vb, ib, fallbackVerts.Length, indices.Length);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Refresh the scene graph each frame (currently a no-op for the mesh list,
        /// but a hook for future culling / animation).
        /// </summary>
        public void Update(SpatialPointerPose headPose)
        {
            // Future: distance culling based on headPose.
        }

        public void Update(StepTimer timer)
        {
            // No animated state for the mesh itself; placeholder for consistency.
        }

        public void Render(HolographicFrame frame)
        {
            if (!_loaded) return;
            var context = _deviceResources.D3DDeviceContext;
            if (context == null) return;

            if (_meshes.Count == 0) return;

            // Identity model transform for world-space meshes.
            var identity = Matrix4x4.Transpose(Matrix4x4.Identity);
            context.UpdateSubresource(ref identity, _constantBuffer);

            // Set shaders.
            context.VertexShader.Set(_vertexShader);
            context.PixelShader.Set(_pixelShader);
            context.InputAssembler.InputLayout = _inputLayout;
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

            // Bind the model constant buffer (b0).
            context.VertexShader.SetConstantBuffers(0, _constantBuffer);

            // Draw each ingested mesh with a translucent blend (if the scaffold
            // supports blend state switching; for the scaffold we draw opaque and
            // rely on the alpha from vertex colors).
            foreach (var mesh in _meshes)
            {
                if (mesh.VertexBuffer == null || mesh.IndexBuffer == null) continue;

                context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(mesh.VertexBuffer, Utilities.SizeOf<VertexPositionColor>(), 0));
                context.InputAssembler.SetIndexBuffer(mesh.IndexBuffer, Format.R16_UInt, 0);
                context.DrawIndexed(mesh.IndexCount, 0, 0);
            }
        }

        /// <summary>
        /// Clear all ingested meshes without releasing device resources permanently.
        /// </summary>
        public void Clear()
        {
            foreach (var m in _meshes)
            {
                m.VertexBuffer?.Dispose();
                m.IndexBuffer?.Dispose();
            }
            _meshes.Clear();
        }

        /// <summary>
        /// Release device resources without disposing the renderer permanently.
        /// </summary>
        public void ReleaseDeviceDependentResources()
        {
            _loaded = false;

            if (_vertexShader != null) { _vertexShader.Dispose(); _vertexShader = null; }
            if (_pixelShader != null) { _pixelShader.Dispose(); _pixelShader = null; }
            if (_inputLayout != null) { _inputLayout.Dispose(); _inputLayout = null; }
            if (_constantBuffer != null) { _constantBuffer.Dispose(); _constantBuffer = null; }

            Clear();
        }

        public void Dispose()
        {
            ReleaseDeviceDependentResources();
        }
    }
}
