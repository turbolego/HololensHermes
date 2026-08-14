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
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using HololensHermes.Common;
using HololensHermes.Models;
using HololensHermes.Services;

namespace HololensHermes.Content
{
    /// <summary>
    /// Renders a 2D floor plan image as a world-locked quad on the real floor.
    ///
    /// The floor plan is loaded from a PNG texture, scaled to real-world meters
    /// (per CalibrationService transform), and compass-rotated (via CompassService)
    /// so the building's orientation stays correct as the user turns.
    ///
    /// Uses the existing holographic scaffold conventions:
    ///   - ModelConstantBuffer (b0) for the per-frame model transform.
    ///   - ViewProjectionConstantBuffer (b1) is managed by CameraResources.
    ///   - Vertex layout: VertexPositionTexture (POSITION + TEXCOORD0).
    ///   - Textures are created as D3D11 Texture2D from SharpDX.Direct3D11.
    /// </summary>
    internal sealed class FloorPlanRenderer
    {
        private readonly DeviceResources _deviceResources;
        private bool _loaded;

        // Shaders and pipeline objects.
        private VertexShader _vertexShader;
        private PixelShader _pixelShader;
        private InputLayout _inputLayout;
        private Buffer _constantBuffer;

        // Floor plan quad geometry.
        private Buffer _vertexBuffer;
        private Buffer _indexBuffer;
        private int _indexCount;

        // Floor plan texture + sampler.
        private Texture2D _floorPlanTexture;
        private ShaderResourceView _floorPlanTextureSRV;
        private SamplerState _samplerState;

        // Current model transform for this frame.
        private Matrix4x4 _model;

        // Per-frame animation state for gentle fade-in / pulse.
        private float _elapsedSeconds;

        // Quad half-extents in world units (meters). Set by UpdateWorldTransform.
        private float _halfWidth;
        private float _halfHeight;

        public FloorPlanRenderer(DeviceResources deviceResources)
        {
            _deviceResources = deviceResources;
            _loaded = false;
        }

        /// <summary>
        /// Create device-dependent resources: shaders, input layout, sampler,
        /// constant buffer, and the quad vertex/index buffers.
        ///
        /// Texture loading is intentionally deferred to LoadTextureAsync so
        /// that the floor plan image can be fetched asynchronously (network or
        /// app-local assets) without blocking the render thread.
        /// </summary>
        public async Task CreateDeviceDependentResourcesAsync()
        {
            var device = _deviceResources.D3DDevice;
            if (device == null)
            {
                return;
            }

            // The FxCompile target produces deployed .cso assets.
            var vertexBytecode = await ShaderBytecodeLoader.LoadAsync("Content/Shaders/FloorPlanVertexShader.cso");
            var pixelBytecode = await ShaderBytecodeLoader.LoadAsync("Content/Shaders/FloorPlanPixelShader.cso");
            _vertexShader = new VertexShader(device, vertexBytecode);
            _pixelShader = new PixelShader(device, pixelBytecode);

            // Input layout: POSITION (3 floats) + TEXCOORD0 (2 floats).
            _inputLayout = new InputLayout(
                device,
                vertexBytecode,
                new[]
                {
                    new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                    new InputElement("TEXCOORD", 0, Format.R32G32_Float, 12, 0)
                });

            // Constant buffer for the model transform.
            _constantBuffer = new Buffer(
                device,
                64, // sizeof(Matrix4x4)
                ResourceUsage.Dynamic,
                BindFlags.ConstantBuffer,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                64);

            // Sampler state: linear filtering, clamp to edge.
            _samplerState = new SamplerState(device,
                new SamplerStateDescription
                {
                    Filter = Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Clamp,
                    MipLodBias = 0f,
                    MaximumAnisotropy = 16,
                    ComparisonFunction = Comparison.Always,
                    BorderColor = new SharpDX.Mathematics.Color4(0f, 0f, 0f, 0f),
                    MinimumLod = 0,
                    MaximumLod = float.MaxValue
                });

            // Build the quad vertex and index buffers.
            BuildQuadBuffers(device);

            _loaded = true;
        }

        /// <summary>
        /// Asynchronously load the floor plan PNG from a URI into a D3D11 texture.
        ///
        /// uri examples:
        ///   - ms-appx:///Assets/FloorPlan.png
        ///   - https://example.com/floorplan.png
        ///
        /// The texture is bound to t0 in the pixel shader.
        /// </summary>
        public async Task LoadTextureAsync(string uri)
        {
            var device = _deviceResources.D3DDevice;
            if (device == null) return;

            // Release any previously loaded texture.
            if (_floorPlanTexture != null)
            {
                _floorPlanTexture.Dispose();
                _floorPlanTexture = null;
            }
            if (_floorPlanTextureSRV != null)
            {
                _floorPlanTextureSRV.Dispose();
                _floorPlanTextureSRV = null;
            }

            var file = await LoadFileAsync(uri);
            if (file == null) return;

            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var pixels = pixelData.DetachPixelData();
                using (var data = SharpDX.DataStream.Create(pixels, true, false))
                {
                    var textureDescription = new Texture2DDescription
                    {
                        Width = decoder.PixelWidth,
                        Height = decoder.PixelHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                        Usage = ResourceUsage.Immutable,
                        BindFlags = BindFlags.ShaderResource,
                        CpuAccessFlags = CpuAccessFlags.None,
                        OptionFlags = ResourceOptionFlags.None
                    };
                    var dataRectangle = new SharpDX.DataRectangle(data.DataPointer, (int)decoder.PixelWidth * 4);
                    _floorPlanTexture = new Texture2D(device, textureDescription, dataRectangle);
                }

                _floorPlanTextureSRV = new ShaderResourceView(device, _floorPlanTexture);
            }
        }

        private static async Task<Windows.Storage.StorageFile> LoadFileAsync(string uri)
        {
            if (uri.StartsWith("ms-appx:///"))
            {
                var appUri = new Uri(uri);
                return await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(appUri);
            }

            // Network loading: download to a temp file then return it.
            var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var bytes = await http.GetByteArrayAsync(uri);
            var tmp = Windows.Storage.ApplicationData.Current.LocalFolder;
            var file = await tmp.CreateFileAsync(
                "floorplan_temp.png",
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            {
                var writer = new DataWriter(stream);
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            return file;
        }

        private void BuildQuadBuffers(SharpDX.Direct3D11.Device device)
        {
            // Quad in local space: XZ plane, Y = 0, centered at origin.
            // UVs go from (0,0) at top-left to (1,1) at bottom-right.
            var vertices = new[]
            {
                new VertexPositionTexture(new Vector3(-1f, 0f, -1f), new Vector2(0f, 0f)),
                new VertexPositionTexture(new Vector3( 1f, 0f, -1f), new Vector2(1f, 0f)),
                new VertexPositionTexture(new Vector3( 1f, 0f,  1f), new Vector2(1f, 1f)),
                new VertexPositionTexture(new Vector3(-1f, 0f,  1f), new Vector2(0f, 1f))
            };

            _vertexBuffer = Buffer.Create(device, BindFlags.VertexBuffer, vertices);

            ushort[] indices = { 0, 1, 2, 0, 2, 3 };
            _indexBuffer = Buffer.Create(device, BindFlags.IndexBuffer, indices);
            _indexCount = indices.Length;
        }

        /// <summary>
        /// Update the floor plan's world transform for this frame.
        ///
        /// worldCenter     = the world position at which the floor plan is anchored.
        /// floorPlanTransform = the AffineFloorPlanTransform from CalibrationService (image→world).
        /// compassHeadingDeg = current compass heading (via CompassService), applied as a Y-rotation
        ///                     so the floor plan stays north-aligned as the user turns.
        ///
        /// The floor plan is rendered as a quad on the floor (Y = floor height).
        /// The quad is sized to the floor plan's real-world extent.
        /// </summary>
        public void UpdateWorldTransform(Vector3 worldCenter,
                                          AffineFloorPlanTransform floorPlanTransform,
                                          float compassHeadingDeg)
        {
            // Size the quad to the floor plan's real-world extent.
            // If the transform hasn't been calibrated yet, use a default 10m x 10m quad.
            float widthMeters  = 10f;
            float heightMeters = 10f;
            if (floorPlanTransform != null && floorPlanTransform.Scale > 0f)
            {
                // The scale encodes image-pixels -> world-units (meters).
                // A 1000-pixel-wide image at scale 0.01 -> 10 meters.
                // Use the transform scale to derive approximate extents.
                widthMeters  = 1000f * floorPlanTransform.Scale;
                heightMeters = 1000f * floorPlanTransform.Scale;
            }

            _halfWidth  = widthMeters  * 0.5f;
            _halfHeight = heightMeters * 0.5f;

            // Start from a quad in local space: XZ plane centered at worldCenter.
            // Apply compass rotation (Y-axis) so north stays north as the user turns.
            float theta = -compassHeadingDeg * ((float)Math.PI / 180f);
            float c = (float)Math.Cos(theta);
            float s = (float)Math.Sin(theta);

            // Rotation around Y:
            //   x' = x*c + z*s
            //   z' = -x*s + z*c
            var rot = new Matrix4x4(
                c,  0f, s, 0f,
                0f, 1f, 0f, 0f,
               -s,  0f, c, 0f,
                0f, 0f, 0f, 1f);

            // Apply the floor-plan calibration transform: scale + rotation + translation.
            // The calibration transform maps image-space points to world space.
            // We compose: model = T * R_cal * R_compass * scale_factor * quad_local
            float calScale = floorPlanTransform.Scale;
            if (calScale <= 0f) calScale = 1f;

            var calRot = Matrix4x4.CreateRotationY(floorPlanTransform.RotationRadians);
            var calTrans = Matrix4x4.CreateTranslation(floorPlanTransform.Translation);

            // Scale the unit quad to the real-world size, then apply compass + calibration.
            var scale = Matrix4x4.CreateScale(new Vector3(_halfWidth, 1f, _halfHeight));
            _model = calTrans * calRot * rot * scale;
            _model = Matrix4x4.Transpose(_model); // shaders expect row-major transposed
        }

        /// <summary>
        /// Animate per-frame: accumulate elapsed time for any future pulse/fade effects.
        /// </summary>
        public void Update(StepTimer timer)
        {
            _elapsedSeconds = (float)timer.TotalSeconds;
        }

        /// <summary>
        /// Render the floor plan quad.
        /// </summary>
        public void Render(HolographicFrame frame)
        {
            if (!_loaded) return;
            var device = _deviceResources.D3DDevice;
            if (device == null) return;
            var context = _deviceResources.D3DDeviceContext;
            if (context == null) return;

            // Update the model constant buffer.
            context.UpdateSubresource(ref _model, _constantBuffer);

            // Set shaders and layout.
            context.VertexShader.Set(_vertexShader);
            context.PixelShader.Set(_pixelShader);
            context.InputAssembler.InputLayout = _inputLayout;
            context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

            // Set vertex and index buffers.
            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBuffer, Utilities.SizeOf<VertexPositionTexture>(), 0));
            context.InputAssembler.SetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);

            // Bind the model constant buffer (b0).
            context.VertexShader.SetConstantBuffers(0, _constantBuffer);

            // Bind the floor plan texture and sampler (t0, s0).
            if (_floorPlanTextureSRV != null)
            {
                context.PixelShader.SetShaderResource(0, _floorPlanTextureSRV);
            }
            context.PixelShader.SetSampler(0, _samplerState);

            // Draw the quad.
            context.DrawIndexed(_indexCount, 0, 0);
        }

        /// <summary>
        /// Release device resources without disposing the renderer permanently.
        /// Called on device loss; resources will be recreated on device restore.
        /// </summary>
        public void ReleaseDeviceDependentResources()
        {
            _loaded = false;

            if (_vertexShader != null) { _vertexShader.Dispose(); _vertexShader = null; }
            if (_pixelShader != null) { _pixelShader.Dispose(); _pixelShader = null; }
            if (_inputLayout != null) { _inputLayout.Dispose(); _inputLayout = null; }
            if (_constantBuffer != null) { _constantBuffer.Dispose(); _constantBuffer = null; }
            if (_vertexBuffer != null) { _vertexBuffer.Dispose(); _vertexBuffer = null; }
            if (_indexBuffer != null) { _indexBuffer.Dispose(); _indexBuffer = null; }
            if (_floorPlanTexture != null) { _floorPlanTexture.Dispose(); _floorPlanTexture = null; }
            if (_floorPlanTextureSRV != null) { _floorPlanTextureSRV.Dispose(); _floorPlanTextureSRV = null; }
            if (_samplerState != null) { _samplerState.Dispose(); _samplerState = null; }

            _indexCount = 0;
        }

        /// <summary>
        /// Dispose of all resources permanently.
        /// </summary>
        public void Dispose()
        {
            ReleaseDeviceDependentResources();
        }
    }
}
