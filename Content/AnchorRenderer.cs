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
using Windows.Graphics.Holographic;
using HololensHermes.Common;
using HololensHermes.Content;
using HololensHermes.Services;

namespace HololensHermes.Content
{
    /// <summary>
    /// Renders a world-locked target marker (pulsing billboard + arrow) at a POI anchor.
    ///
    /// The anchor is a SpatialAnchor placed by AnchorStoreService at the target
    /// position returned by Hermes API (HermesTarget X/Y in floor-plan world space).
    ///
    /// The marker stays world-locked across user movement, and CompassService rotates
    /// the arrow direction so "forward" always means the direction to the target on the floor.
    ///
    /// Uses the same constant-buffer / shader convention as FloorPlanRenderer.
    /// </summary>
    internal sealed class AnchorRenderer
    {
        private readonly DeviceResources _deviceResources;
        private bool _loaded;

        private VertexShader _vertexShader;
        private PixelShader _pixelShader;
        private InputLayout _inputLayout;
        private Buffer _constantBuffer;

        // Marker geometry: a small quad for the pulsing target, plus an arrow.
        // We render both with a single buffer + index stream for simplicity.
        private Buffer _vertexBuffer;
        private Buffer _indexBuffer;
        private int _indexCount;

        private Texture2D _markerTexture;
        private ShaderResourceView _markerTextureSRV;
        private SamplerState _samplerState;

        private Matrix4x4 _model;

        // Target state.
        private Vector3 _targetWorldPosition;
        private float _targetLabelBillboardHeight; // not implemented as text for now
        private float _pulsePhase;

        public AnchorRenderer(DeviceResources deviceResources)
        {
            _deviceResources = deviceResources;
        }

        public void CreateDeviceDependentResourcesAsync()
        {
            var device = _deviceResources.D3DDevice;
            if (device == null) return;

            _vertexShader = new VertexShader(device, Content.Shaders.AnchorVertexShader.Bytecode);
            _pixelShader  = new PixelShader(device, Content.Shaders.AnchorPixelShader.Bytecode);

            _inputLayout = new InputLayout(
                device,
                Content.Shaders.AnchorVertexShader.Bytecode,
                new[]
                {
                    new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                    new InputElement("COLOR",    0, Format.R32G32B32_Float, 12, 0),
                    new InputElement("TEXCOORD", 0, Format.R32G32_Float,     24, 0)
                });

            _constantBuffer = new Buffer(
                device,
                64,
                ResourceUsage.Dynamic,
                BindFlags.ConstantBuffer,
                CpuAccessFlags.Write,
                ResourceOptionFlags.None,
                64);

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

            BuildMarkerBuffers(device);
            _loaded = true;
        }

        /// <summary>
        /// Asynchronously load a marker icon texture.
        /// </summary>
        public async Task LoadTextureAsync(string uri)
        {
            var device = _deviceResources.D3DDevice;
            if (device == null) return;

            if (_markerTexture != null) { _markerTexture.Dispose(); _markerTexture = null; }
            if (_markerTextureSRV != null) { _markerTextureSRV.Dispose(); _markerTextureSRV = null; }

            var file = await LoadFileAsync(uri);
            if (file == null) return;

            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                var pixelData = await decoder.GetPixelDataAsync(
                    Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Straight,
                    Windows.Graphics.Imaging.BitmapTransform.CreateDefault(),
                    Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
                    Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);

                var surface = pixelData.Direct3DSurface;
                var resource1 = surface as SharpDX.DXGI.Resource1;
                if (resource1 == null) return;

                var texture2D = new Texture2D(device, new SharpDX.Direct3D11.Texture2DDescription
                {
                    Width  = decoder.BitmapPixelWidth,
                    Height = decoder.BitmapPixelHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                    SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                });

                using (var ctx = device.ImmediateContext)
                {
                    var readBack = new Texture2D(device, new SharpDX.Direct3D11.Texture2DDescription
                    {
                        Width = decoder.BitmapPixelWidth,
                        Height = decoder.BitmapPixelHeight,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = SharpDX.DXGI.Format.B8G8R8A8_UNorm,
                        SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CpuAccessFlags = CpuAccessFlags.Read,
                        OptionFlags = ResourceOptionFlags.None
                    });

                    ctx.CopyResource(surface, readBack);
                    var map = ctx.MapSubresource(readBack, 0, SharpDX.DataBox.Empty);
                    using (var mem = SharpDX.DataStream.Create(
                        (int)(decoder.BitmapPixelWidth * decoder.BitmapPixelHeight * 4),
                        SharpDX.DataStreamFlags.None))
                    {
                        var rowPitch = (int)map.RowPitch;
                        var destPitch = (int)(decoder.BitmapPixelWidth * 4);
                        for (int y = 0; y < decoder.BitmapPixelHeight; y++)
                        {
                            var srcRow = (byte*)map.DataPointer + y * rowPitch;
                            mem.Write(srcRow, destPitch);
                        }
                        mem.Position = 0;
                        ctx.UpdateSubresource(texture2D, 0, null, mem, 0, 0);
                    }
                    map.Dispose();
                    ctx.UnmapSubresource(readBack, 0);
                    readBack.Dispose();
                }

                _markerTexture = texture2D;
                _markerTextureSRV = new ShaderResourceView(device, _markerTexture);
            }
        }

        private static async Task<Windows.Storage.StorageFile> LoadFileAsync(string uri)
        {
            if (uri.StartsWith("ms-appx:///"))
            {
                return await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
            }

            var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var bytes = await http.GetByteArrayAsync(uri);
            var tmp = Windows.Storage.ApplicationData.Current.LocalFolder;
            var file = await tmp.CreateFileAsync(
                "anchor_temp.png",
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            {
                var writer = new Windows.Storage.Streams.DataWriter(stream);
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }
            return file;
        }

        private void BuildMarkerBuffers(SharpDX.Direct3D11.Device device)
        {
            // A small quad for the marker + a 2D arrow pointing upward (toward target).
            // Both share the same buffer to keep the draw call count low.
            float quadSize = 0.3f;   // 30 cm marker
            float arrowLen = 0.6f;
            float arrowWid = 0.1f;

            var verts = new List<VertexPositionColorTexture>();

            // Marker quad (centered at origin).
            float hs = quadSize * 0.5f;
            verts.Add(new VertexPositionColorTexture(new Vector3(-hs, 0f, -hs), new Vector3(1f, 0.3f, 0.2f), new Vector2(0f, 0f)));
            verts.Add(new VertexPositionColorTexture(new Vector3( hs, 0f, -hs), new Vector3(1f, 0.3f, 0.2f), new Vector2(1f, 0f)));
            verts.Add(new VertexPositionColorTexture(new Vector3( hs, 0f,  hs), new Vector3(1f, 0.3f, 0.2f), new Vector2(1f, 1f)));
            verts.Add(new VertexPositionColorTexture(new Vector3(-hs, 0f,  hs), new Vector3(1f, 0.3f, 0.2f), new Vector2(0f, 1f)));

            // Arrow: a triangle pointing up (+Z) from the marker center.
            verts.Add(new VertexPositionColorTexture(new Vector3(0f, 0f, hs + arrowLen), new Vector3(1f, 0.9f, 0.4f), new Vector2(0.5f, 1f)));
            verts.Add(new VertexPositionColorTexture(new Vector3(-arrowWid, 0f, hs),     new Vector3(1f, 0.9f, 0.4f), new Vector2(0f, 0f)));
            verts.Add(new VertexPositionColorTexture(new Vector3( arrowWid, 0f, hs),     new Vector3(1f, 0.9f, 0.4f), new Vector2(1f, 0f)));

            using (var context = device.ImmediateContext)
            {
                _vertexBuffer = new Buffer(device, verts, Utilities.SizeOf<VertexPositionColorTexture>() * verts.Count,
                    ResourceUsage.Immutable, BindFlags.VertexBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, Utilities.SizeOf<VertexPositionColorTexture>());
            }

            ushort[] indices = { 0, 1, 2, 0, 2, 3, 4, 5, 6 };
            using (var context = device.ImmediateContext)
            {
                _indexBuffer = new Buffer(device, indices, Utilities.SizeOf<ushort>() * indices.Length,
                    ResourceUsage.Immutable, BindFlags.IndexBuffer, CpuAccessFlags.None, ResourceOptionFlags.None, Utilities.SizeOf<ushort>());
            }
            _indexCount = indices.Length;
        }

        /// <summary>
        /// Set the anchor world position and label. Called when Hermes resolves a goal.
        /// </summary>
        public void SetTarget(Vector3 worldPosition, string label)
        {
            _targetWorldPosition = worldPosition;
        }

        public void SetInvisible()
        {
            // Hide the marker by moving it far away.
            _targetWorldPosition = new Vector3(0f, 0f, -10000f);
        }

        /// <summary>
        /// Update the anchor's world transform for this frame.
        /// </summary>
        public void UpdateWorldTransform(Vector3 floorPlanWorldCenter,
                                          AffineFloorPlanTransform floorPlanTransform,
                                          float compassHeadingDeg)
        {
            // The target is world-locked; the compass heading is used to orient any
            // directional indicator, but the marker position itself is in world space.
            // Compose model: translate to target position, apply pulse.
            float pulse = 1f + 0.05f * (float)Math.Sin(_pulsePhase);
            var scale = Matrix4x4.CreateScale(new Vector3(pulse, pulse, pulse));
            var trans = Matrix4x4.CreateTranslation(_targetWorldPosition);

            _model = Matrix4x4.Transpose(trans * scale);
        }

        public void Update(StepTimer timer)
        {
            _pulsePhase += (float)timer.ElapsedSeconds * 4f; // fast pulse
        }

        public void Render(HolographicFrame frame)
        {
            if (!_loaded) return;
            var device = _deviceResources.D3DDevice;
            if (device == null) return;
            var context = _deviceResources.D3DDeviceContext;
            if (context == null) return;

            context.UpdateSubresource(ref _model, _constantBuffer);

            context.VertexShader.Set(_vertexShader);
            context.PixelShader.Set(_pixelShader);
            context.InputAssembler.InputLayout = _inputLayout;
            context.InputAssembler.SetPrimitiveTopology(PrimitiveTopology.TriangleList);

            context.InputAssembler.SetVertexBuffers(0, new VertexBufferBinding(_vertexBuffer, Utilities.SizeOf<VertexPositionColorTexture>(), 0));
            context.InputAssembler.SetIndexBuffer(_indexBuffer, Format.R16_UInt, 0);

            context.VertexShader.SetConstantBuffers(0, _constantBuffer);

            if (_markerTextureSRV != null)
            {
                context.PixelShader.SetShaderResource(0, _markerTextureSRV);
            }
            context.PixelShader.SetSampler(0, _samplerState);

            context.DrawIndexed(_indexCount, 0, 0);
        }

        public void ReleaseDeviceDependentResources()
        {
            _loaded = false;

            if (_vertexShader != null) { _vertexShader.Dispose(); _vertexShader = null; }
            if (_pixelShader != null) { _pixelShader.Dispose(); _pixelShader = null; }
            if (_inputLayout != null) { _inputLayout.Dispose(); _inputLayout = null; }
            if (_constantBuffer != null) { _constantBuffer.Dispose(); _constantBuffer = null; }
            if (_vertexBuffer != null) { _vertexBuffer.Dispose(); _vertexBuffer = null; }
            if (_indexBuffer != null) { _indexBuffer.Dispose(); _indexBuffer = null; }
            if (_markerTexture != null) { _markerTexture.Dispose(); _markerTexture = null; }
            if (_markerTextureSRV != null) { _markerTextureSRV.Dispose(); _markerTextureSRV = null; }
            if (_samplerState != null) { _samplerState.Dispose(); _samplerState = null; }

            _indexCount = 0;
        }

        public void Dispose()
        {
            ReleaseDeviceDependentResources();
        }

        private struct VertexPositionColorTexture
        {
            public VertexPositionColorTexture(Vector3 pos, Vector3 color, Vector2 uv)
            {
                this.pos  = pos;
                this.color = color;
                this.uv    = uv;
            }

            public Vector3 pos;
            public Vector3 color;
            public Vector2 uv;
        }
    }
}
