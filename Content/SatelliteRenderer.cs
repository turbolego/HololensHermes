using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using HololensSatelliteViewer.Common;
using HololensSatelliteViewer.Models;
using HololensSatelliteViewer.Services;
using Windows.UI.Input.Spatial;
using Windows.Devices.Sensors;

namespace HololensSatelliteViewer.Content
{
    internal class SatelliteRenderer : Disposer
    {
        private readonly DeviceResources deviceResources;
        private readonly OrbitService orbitService;
        private readonly GeolocationService geolocationService;
        private readonly CompassService compassService;

        private SharpDX.Direct3D11.InputLayout inputLayout;
        private SharpDX.Direct3D11.Buffer vertexBuffer;
        private SharpDX.Direct3D11.Buffer indexBuffer;
        private SharpDX.Direct3D11.VertexShader vertexShader;
        private SharpDX.Direct3D11.GeometryShader geometryShader;
        private SharpDX.Direct3D11.PixelShader pixelShader;
        private SharpDX.Direct3D11.Buffer modelConstantBuffer;

        private SharpDX.Direct3D11.InputLayout textInputLayout;
        private SharpDX.Direct3D11.Buffer textVertexBuffer;
        private SharpDX.Direct3D11.Buffer textIndexBuffer;
        private SharpDX.Direct3D11.VertexShader textVertexShader;
        private SharpDX.Direct3D11.GeometryShader textGeometryShader;
        private SharpDX.Direct3D11.PixelShader textPixelShader;
        private SharpDX.Direct3D11.SamplerState textSampler;
        private SharpDX.Direct3D11.ShaderResourceView glyphAtlasSrv;

        private ModelConstantBuffer modelConstantBufferData;
        private int indexCount;
        private bool loadingComplete;
        private bool usingVprtShaders;

        private bool fetchInProgress;
        private DateTime lastFetchUtc = DateTime.MinValue;
        private volatile List<Satellite> satellites = new List<Satellite>();

        private Vector3 currentHeadPosition = Vector3.Zero;
        private Vector3 worldCenter = Vector3.Zero;
        private bool worldCenterLocked;
        private float ceilingY;

        /// <summary>
        /// Latest compass heading in degrees (0=North, 90=East, 180=South, 270=West).
        /// Updated on a background thread by CompassService; read on the render thread.
        /// </summary>
        private float compassHeadingDegrees;

        private string gpsDebug = "GPS: --";

        private const int MaxSatellitesRendered = 10;
        private const float SatCubeScale = 0.25f;
        private const float DomeRadiusMeters = 2.5f;
        private const float CeilingOffset = 1.4f;
        private const float CeilingClearance = 0.3f;
        private const float SatelliteLabelSize = 0.10f;
        private const float DebugTextSize = 0.08f;
        private const float DebugLineSpacing = 0.08f;

        private const int AtlasCols = 16;
        private const int AtlasRows = 8;
        private const int GlyphCellW = 16;
        private const int GlyphCellH = 24;

        private readonly Dictionary<int, TrackState> tracks = new Dictionary<int, TrackState>();

        private static readonly Dictionary<char, ushort> GlyphBits = new Dictionary<char, ushort>
        {
            {'A', 0b_010_101_111_101_101}, {'B', 0b_110_101_110_101_110}, {'C', 0b_011_100_100_100_011},
            {'D', 0b_110_101_101_101_110}, {'E', 0b_111_100_110_100_111}, {'F', 0b_111_100_110_100_100},
            {'G', 0b_011_100_101_101_011}, {'H', 0b_101_101_111_101_101}, {'I', 0b_111_010_010_010_111},
            {'J', 0b_001_001_001_101_010}, {'K', 0b_101_101_110_101_101}, {'L', 0b_100_100_100_100_111},
            {'M', 0b_101_111_101_101_101}, {'N', 0b_101_111_111_111_101}, {'O', 0b_010_101_101_101_010},
            {'P', 0b_110_101_110_100_100}, {'Q', 0b_010_101_101_111_011}, {'R', 0b_110_101_110_110_101},
            {'S', 0b_011_100_010_001_110}, {'T', 0b_111_010_010_010_010}, {'U', 0b_101_101_101_101_111},
            {'V', 0b_101_101_101_101_010}, {'W', 0b_101_101_111_111_101}, {'X', 0b_101_101_010_101_101},
            {'Y', 0b_101_101_010_010_010}, {'Z', 0b_111_001_010_100_111}, {'0', 0b_111_101_101_101_111},
            {'1', 0b_010_110_010_010_111}, {'2', 0b_111_001_111_100_111}, {'3', 0b_111_001_111_001_111},
            {'4', 0b_101_101_111_001_001}, {'5', 0b_111_100_111_001_111}, {'6', 0b_111_100_111_101_111},
            {'7', 0b_111_001_010_010_010}, {'8', 0b_111_101_111_101_111}, {'9', 0b_111_101_111_001_111},
            {'-', 0b_000_000_111_000_000}, {'_', 0b_000_000_000_000_111}, {'.', 0b_000_000_000_000_010},
            {':', 0b_000_010_000_010_000}, {',', 0b_000_000_000_010_100}, {' ', 0}
        };

        public SatelliteRenderer(DeviceResources deviceResources)
        {
            this.deviceResources = deviceResources;
            this.orbitService = new OrbitService();
            this.geolocationService = new GeolocationService();
            this.compassService = new CompassService();
            this.compassService.Initialize();
            CreateDeviceDependentResourcesAsync();
        }

        public void PositionHologram(SpatialPointerPose pointerPose)
        {
            if (pointerPose == null)
            {
                return;
            }

            currentHeadPosition = pointerPose.Head.Position;
            if (!worldCenterLocked)
            {
                worldCenter = currentHeadPosition;
                ceilingY = worldCenter.Y + CeilingOffset;
                worldCenterLocked = true;
            }
        }

        public async void Update(StepTimer timer)
        {
            if (!fetchInProgress && (DateTime.UtcNow - lastFetchUtc).TotalSeconds >= 1.0)
            {
                fetchInProgress = true;
                try
                {
                    var gps = await geolocationService.GetCurrentLocationAsync();
                    if (gps != null)
                    {
                        var lat = gps.Coordinate.Point.Position.Latitude;
                        var lon = gps.Coordinate.Point.Position.Longitude;
                        var altKm = gps.Coordinate.Point.Position.Altitude / 1000.0;
                        orbitService.SetObserverLocation(lat, lon, altKm);
                        gpsDebug = string.Format(CultureInfo.InvariantCulture, "GPS {0:F3},{1:F3}", lat, lon);
                    }

                    var live = await orbitService.GetLiveSatellitesAsync();
                    // Rank by elevation (best-visible first), NOT by range:
                    // range-ranking would always drop geostationary satellites
                    // (GOES etc., ~35,000+ km away) in favor of nearby LEO
                    // objects, even when the GEO bird is high in the sky.
                    // Elevation drives the dome position; range is irrelevant
                    // to rendering here.
                    var closest = SatelliteSelection.BestVisible(
                        live, MaxSatellitesRendered);

                    satellites = closest;

                    lastFetchUtc = DateTime.UtcNow;
                }
                catch
                {
                }
                finally
                {
                    fetchInProgress = false;
                }
            }
        }

        public void Render()
        {
            if (!loadingComplete || !worldCenterLocked)
            {
                return;
            }

            RenderSatellites();
            RenderText();
        }

        public Vector3 Position => worldCenter;

        /// <summary>
        /// Set by the main loop each frame from the compass service.
        /// </summary>
        public void SetCompassHeading(float degrees)
        {
            compassHeadingDegrees = degrees;
        }

        private void RenderSatellites()
        {
            var context = deviceResources.D3DDeviceContext;

            context.InputAssembler.InputLayout = inputLayout;
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new SharpDX.Direct3D11.VertexBufferBinding(vertexBuffer, SharpDX.Utilities.SizeOf<VertexPositionColor>(), 0));
            context.InputAssembler.SetIndexBuffer(indexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);

            context.VertexShader.SetShader(vertexShader, null, 0);
            context.VertexShader.SetConstantBuffers(0, modelConstantBuffer);
            if (!usingVprtShaders)
            {
                context.GeometryShader.SetShader(geometryShader, null, 0);
            }
            context.PixelShader.SetShader(pixelShader, null, 0);

            foreach (var sat in satellites)
            {
                var pos = ComputeSatellitePosition(sat);
                DrawCubeAt(pos, SatCubeScale, compassHeadingDegrees);
            }
        }

        private void RenderText()
        {
            var context = deviceResources.D3DDeviceContext;

            context.InputAssembler.InputLayout = textInputLayout;
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0, new SharpDX.Direct3D11.VertexBufferBinding(textVertexBuffer, SharpDX.Utilities.SizeOf<TextVertex>(), 0));
            context.InputAssembler.SetIndexBuffer(textIndexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);

            context.VertexShader.SetShader(textVertexShader, null, 0);
            context.VertexShader.SetConstantBuffers(0, modelConstantBuffer);
            if (!usingVprtShaders)
            {
                context.GeometryShader.SetShader(textGeometryShader, null, 0);
            }
            context.PixelShader.SetShader(textPixelShader, null, 0);
            context.PixelShader.SetShaderResource(0, glyphAtlasSrv);
            context.PixelShader.SetSampler(0, textSampler);

            foreach (var sat in satellites)
            {
                var pos = ComputeSatellitePosition(sat) + new Vector3(0f, 0.08f, 0f);
                DrawTextBillboard(ShortName(sat.Name), pos, SatelliteLabelSize, true, compassHeadingDegrees);
            }

            Vector3 panelCenter = worldCenter + new Vector3(0.0f, 0.15f, -1.15f);
            var lines = new List<string>();

            lines.Add(gpsDebug);
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "TLE:{0} PROP:{1} VIS:{2} T:{3:ss}",
                orbitService.TleCount,
                orbitService.PropagatedCount,
                orbitService.AboveHorizon,
                DateTime.UtcNow));

            // ECI sanity-check row
            if (!string.IsNullOrEmpty(orbitService.EciDebug))
                lines.Add(orbitService.EciDebug.Length > 28
                    ? orbitService.EciDebug.Substring(0, 28)
                    : orbitService.EciDebug);

            // Error row
            if (!string.IsNullOrEmpty(orbitService.LastError))
                lines.Add(orbitService.LastError.Length > 28
                    ? orbitService.LastError.Substring(0, 28)
                    : orbitService.LastError);

            foreach (var sat in satellites)
            {
                var pos  = ComputeSatellitePosition(sat);
                float relX = pos.X - worldCenter.X;
                float relZ = pos.Z - worldCenter.Z;
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} A{1:F0} E{2:F0} X{3:F1} Z{4:F1}",
                    ShortName(sat.Name), sat.Azimuth, sat.Elevation, relX, relZ));
            }

            float startY = panelCenter.Y + 0.35f;
            for (int i = 0; i < lines.Count && i < 11; i++)
            {
                Vector3 p = new Vector3(panelCenter.X - 0.7f, startY - i * DebugLineSpacing, panelCenter.Z);
                DrawTextBillboard(Sanitize(lines[i]), p, DebugTextSize, false, compassHeadingDegrees);
            }

            context.PixelShader.SetShaderResource(0, null);
        }

        private Vector3 ComputeSatellitePosition(Satellite sat)
        {
            double az = sat.Azimuth * Math.PI / 180.0;
            double el = sat.Elevation * Math.PI / 180.0;

            // Use azimuth directly for now (no smoothing) to see spread
            float useAz = (float)az;

            // Calculate horizontal distance - use larger multiplier for better spread
            // Even low elevation satellites should be well separated by azimuth
            float horizontal = DomeRadiusMeters * (float)Math.Max(0.5, Math.Cos(el));
            float x = (float)Math.Sin(useAz) * horizontal;
            float z = (float)(-Math.Cos(useAz)) * horizontal;

            // Keep satellites near ceiling, with vertical spread based on elevation
            float y = (ceilingY - CeilingClearance) + (float)Math.Sin(el) * 0.4f;

            // Only above local horizon and above floor relative to center.
            if (sat.Elevation <= 0.0 || y < worldCenter.Y - 0.1f)
            {
                y = worldCenter.Y - 0.1f;
            }

            return new Vector3(worldCenter.X + x, y, worldCenter.Z + z);
        }

        private void DrawCubeAt(Vector3 worldPos, float scale, float compassHeadingDegrees)
        {
            // Rotate the world position around Y axis by compass heading so the
            // satellite dome tracks the user's physical orientation.
            // When compassHeadingDegrees=0 (facing North), no rotation — the dome
            // is in "world" orientation. When heading=90 (facing East), the dome
            // shifts so that what was "North" in the dome now appears to the user's
            // right, matching real-world alignment.
            float headingRad = (float)(compassHeadingDegrees * Math.PI / 180.0);
            Matrix4x4 compassRot = Matrix4x4.CreateRotationY(headingRad);

            // Apply compass rotation to the world position, then build the model matrix
            Vector3 rotatedPos = Vector3.Transform(worldPos, compassRot);
            Matrix4x4 m = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(rotatedPos);
            modelConstantBufferData.model = Matrix4x4.Transpose(m);
            deviceResources.D3DDeviceContext.UpdateSubresource(ref modelConstantBufferData, modelConstantBuffer);
            deviceResources.D3DDeviceContext.DrawIndexedInstanced(indexCount, 2, 0, 0, 0);
        }

        private void DrawTextBillboard(string text, Vector3 origin, float size, bool faceCamera, float compassHeadingDegrees)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            float advance = size * 0.60f;
            float start = -((text.Length - 1) * advance) * 0.5f;

            for (int i = 0; i < text.Length; i++)
            {
                char c = char.ToUpperInvariant(text[i]);
                var uv = GetGlyphUv(c);
                UpdateTextQuadVertices(uv);

                Vector3 glyphPos = origin + new Vector3(start + i * advance, 0, 0);

                // Rotate the glyph position by compass heading so labels track the dome
                float headingRad = (float)(compassHeadingDegrees * Math.PI / 180.0);
                Matrix4x4 compassRot = Matrix4x4.CreateRotationY(headingRad);
                Vector3 rotatedPos = Vector3.Transform(glyphPos, compassRot);

                Matrix4x4 m = faceCamera
                    ? BuildBillboard(rotatedPos, size * 0.55f, size * 0.85f)
                    : Matrix4x4.CreateScale(size * 0.55f, size * 0.85f, 1.0f) * Matrix4x4.CreateTranslation(rotatedPos);

                modelConstantBufferData.model = Matrix4x4.Transpose(m);
                deviceResources.D3DDeviceContext.UpdateSubresource(ref modelConstantBufferData, modelConstantBuffer);
                deviceResources.D3DDeviceContext.DrawIndexedInstanced(6, 2, 0, 0, 0);
            }
        }

        private Matrix4x4 BuildBillboard(Vector3 pos, float sx, float sy)
        {
            Vector3 forward = Vector3.Normalize(currentHeadPosition - pos);
            if (forward.LengthSquared() < 1e-6f)
            {
                forward = new Vector3(0, 0, -1);
            }

            Vector3 upWorld = Vector3.UnitY;
            Vector3 right = Vector3.Normalize(Vector3.Cross(upWorld, forward));
            if (right.LengthSquared() < 1e-6f)
            {
                right = Vector3.UnitX;
            }
            Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));

            return new Matrix4x4(
                right.X * sx, right.Y * sx, right.Z * sx, 0,
                up.X * sy, up.Y * sy, up.Z * sy, 0,
                forward.X, forward.Y, forward.Z, 0,
                pos.X, pos.Y, pos.Z, 1);
        }

        private void UpdateTextQuadVertices(UvRect uv)
        {
            var verts = new[]
            {
                new TextVertex(new Vector3(-0.5f,  0.5f, 0), new Vector2(uv.U0, uv.V0)),
                new TextVertex(new Vector3( 0.5f,  0.5f, 0), new Vector2(uv.U1, uv.V0)),
                new TextVertex(new Vector3(-0.5f, -0.5f, 0), new Vector2(uv.U0, uv.V1)),
                new TextVertex(new Vector3( 0.5f, -0.5f, 0), new Vector2(uv.U1, uv.V1))
            };
            deviceResources.D3DDeviceContext.UpdateSubresource(verts, textVertexBuffer);
        }

        private UvRect GetGlyphUv(char c)
        {
            int code = (int)c;
            if (code < 32 || code > 127)
            {
                code = 32;
            }
            int idx = code - 32;
            int col = idx % AtlasCols;
            int row = idx / AtlasCols;
            float atlasW = AtlasCols * GlyphCellW;
            float atlasH = AtlasRows * GlyphCellH;

            float u0 = (col * GlyphCellW) / atlasW;
            float v0 = (row * GlyphCellH) / atlasH;
            float u1 = ((col + 1) * GlyphCellW) / atlasW;
            float v1 = ((row + 1) * GlyphCellH) / atlasH;
            return new UvRect(u0, v0, u1, v1);
        }

        private SharpDX.Direct3D11.Texture2D CreateGlyphAtlasTexture()
        {
            int w = AtlasCols * GlyphCellW;
            int h = AtlasRows * GlyphCellH;
            byte[] pixels = new byte[w * h * 4];

            for (int code = 32; code <= 127; code++)
            {
                char c = (char)code;
                int idx = code - 32;
                int gx = (idx % AtlasCols) * GlyphCellW;
                int gy = (idx / AtlasCols) * GlyphCellH;

                ushort bits;
                if (!GlyphBits.TryGetValue(c, out bits))
                {
                    bits = 0;
                }

                for (int r = 0; r < 5; r++)
                {
                    for (int cc = 0; cc < 3; cc++)
                    {
                        int bitIdx = r * 3 + cc;
                        bool on = ((bits >> (14 - bitIdx)) & 1) == 1;
                        if (!on) continue;

                        int px0 = gx + 2 + cc * 4;
                        int py0 = gy + 3 + r * 4;
                        for (int yy = 0; yy < 3; yy++)
                        {
                            for (int xx = 0; xx < 3; xx++)
                            {
                                int px = px0 + xx;
                                int py = py0 + yy;
                                if (px < 0 || py < 0 || px >= w || py >= h) continue;
                                int p = (py * w + px) * 4;
                                pixels[p + 0] = 255;
                                pixels[p + 1] = 255;
                                pixels[p + 2] = 255;
                                pixels[p + 3] = 255;
                            }
                        }
                    }
                }
            }

            var desc = new SharpDX.Direct3D11.Texture2DDescription
            {
                Width = w,
                Height = h,
                ArraySize = 1,
                MipLevels = 1,
                Format = SharpDX.DXGI.Format.R8G8B8A8_UNorm,
                Usage = SharpDX.Direct3D11.ResourceUsage.Immutable,
                BindFlags = SharpDX.Direct3D11.BindFlags.ShaderResource,
                CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.None,
                SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0)
            };

            var stream = new SharpDX.DataStream(pixels.Length, true, true);
            stream.Write(pixels, 0, pixels.Length);
            stream.Position = 0;
            var box = new SharpDX.DataBox(stream.DataPointer, w * 4, 0);
            var tex = new SharpDX.Direct3D11.Texture2D(deviceResources.D3DDevice, desc, new[] { box });
            stream.Dispose();
            return tex;
        }

        public async void CreateDeviceDependentResourcesAsync()
        {
            ReleaseDeviceDependentResources();
            usingVprtShaders = deviceResources.D3DDeviceSupportsVprt;
            var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;

            // Cube shaders
            var vsFile = usingVprtShaders ? "Content\\Shaders\\VPRTVertexShader.cso" : "Content\\Shaders\\VertexShader.cso";
            var vsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(vsFile));
            vertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(deviceResources.D3DDevice, vsBytes));
            inputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(deviceResources.D3DDevice, vsBytes, new[]
            {
                new SharpDX.Direct3D11.InputElement("POSITION",0,SharpDX.DXGI.Format.R32G32B32_Float,0,0),
                new SharpDX.Direct3D11.InputElement("COLOR",0,SharpDX.DXGI.Format.R32G32B32_Float,12,0)
            }));

            if (!usingVprtShaders)
            {
                var gsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync("Content\\Shaders\\GeometryShader.cso"));
                geometryShader = ToDispose(new SharpDX.Direct3D11.GeometryShader(deviceResources.D3DDevice, gsBytes));
            }
            var psBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync("Content\\Shaders\\PixelShader.cso"));
            pixelShader = ToDispose(new SharpDX.Direct3D11.PixelShader(deviceResources.D3DDevice, psBytes));

            // Text shaders
            var tvsFile = usingVprtShaders ? "Content\\Shaders\\TextVPRTVertexShader.cso" : "Content\\Shaders\\TextVertexShader.cso";
            var tvsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(tvsFile));
            textVertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(deviceResources.D3DDevice, tvsBytes));
            textInputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(deviceResources.D3DDevice, tvsBytes, new[]
            {
                new SharpDX.Direct3D11.InputElement("POSITION",0,SharpDX.DXGI.Format.R32G32B32_Float,0,0),
                new SharpDX.Direct3D11.InputElement("TEXCOORD",0,SharpDX.DXGI.Format.R32G32_Float,12,0)
            }));

            if (!usingVprtShaders)
            {
                var tgsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync("Content\\Shaders\\TextGeometryShader.cso"));
                textGeometryShader = ToDispose(new SharpDX.Direct3D11.GeometryShader(deviceResources.D3DDevice, tgsBytes));
            }
            var tpsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync("Content\\Shaders\\TextPixelShader.cso"));
            textPixelShader = ToDispose(new SharpDX.Direct3D11.PixelShader(deviceResources.D3DDevice, tpsBytes));

            // Cube geometry
            VertexPositionColor[] cubeVerts =
            {
                new VertexPositionColor(new Vector3(-0.03f,-0.03f,-0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3(-0.03f,-0.03f, 0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3(-0.03f, 0.03f,-0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3(-0.03f, 0.03f, 0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3( 0.03f,-0.03f,-0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3( 0.03f,-0.03f, 0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3( 0.03f, 0.03f,-0.03f), new Vector3(1f,0.55f,0f)),
                new VertexPositionColor(new Vector3( 0.03f, 0.03f, 0.03f), new Vector3(1f,0.55f,0f)),
            };
            vertexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(deviceResources.D3DDevice, SharpDX.Direct3D11.BindFlags.VertexBuffer, cubeVerts));

            ushort[] cubeIdx = { 2,1,0,2,3,1, 6,4,5,6,5,7, 0,1,5,0,5,4, 2,6,7,2,7,3, 0,4,6,0,6,2, 1,3,7,1,7,5 };
            indexCount = cubeIdx.Length;
            indexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(deviceResources.D3DDevice, SharpDX.Direct3D11.BindFlags.IndexBuffer, cubeIdx));

            // Text quad geometry
            textVertexBuffer = ToDispose(new SharpDX.Direct3D11.Buffer(deviceResources.D3DDevice,
                new SharpDX.Direct3D11.BufferDescription
                {
                    Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                    BindFlags = SharpDX.Direct3D11.BindFlags.VertexBuffer,
                    SizeInBytes = SharpDX.Utilities.SizeOf<TextVertex>() * 4
                }));

            ushort[] quadIdx = { 0, 1, 2, 2, 1, 3 };
            textIndexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(deviceResources.D3DDevice, SharpDX.Direct3D11.BindFlags.IndexBuffer, quadIdx));

            modelConstantBuffer = ToDispose(new SharpDX.Direct3D11.Buffer(deviceResources.D3DDevice,
                SharpDX.Utilities.SizeOf<ModelConstantBuffer>(),
                SharpDX.Direct3D11.ResourceUsage.Default,
                SharpDX.Direct3D11.BindFlags.ConstantBuffer,
                SharpDX.Direct3D11.CpuAccessFlags.None,
                SharpDX.Direct3D11.ResourceOptionFlags.None,
                0));

            var atlasTex = ToDispose(CreateGlyphAtlasTexture());
            glyphAtlasSrv = ToDispose(new SharpDX.Direct3D11.ShaderResourceView(deviceResources.D3DDevice, atlasTex));

            textSampler = ToDispose(new SharpDX.Direct3D11.SamplerState(deviceResources.D3DDevice,
                new SharpDX.Direct3D11.SamplerStateDescription
                {
                    Filter = SharpDX.Direct3D11.Filter.MinMagMipLinear,
                    AddressU = SharpDX.Direct3D11.TextureAddressMode.Clamp,
                    AddressV = SharpDX.Direct3D11.TextureAddressMode.Clamp,
                    AddressW = SharpDX.Direct3D11.TextureAddressMode.Clamp,
                    ComparisonFunction = SharpDX.Direct3D11.Comparison.Never,
                    BorderColor = new SharpDX.Mathematics.Interop.RawColor4(0, 0, 0, 0),
                    MinimumLod = 0,
                    MaximumLod = float.MaxValue
                }));

            loadingComplete = true;
        }

        public void ReleaseDeviceDependentResources()
        {
            loadingComplete = false;
            DisposeAndNull(ref inputLayout);
            DisposeAndNull(ref vertexBuffer);
            DisposeAndNull(ref indexBuffer);
            DisposeAndNull(ref vertexShader);
            DisposeAndNull(ref geometryShader);
            DisposeAndNull(ref pixelShader);
            DisposeAndNull(ref modelConstantBuffer);

            DisposeAndNull(ref textInputLayout);
            DisposeAndNull(ref textVertexBuffer);
            DisposeAndNull(ref textIndexBuffer);
            DisposeAndNull(ref textVertexShader);
            DisposeAndNull(ref textGeometryShader);
            DisposeAndNull(ref textPixelShader);
            DisposeAndNull(ref textSampler);
            DisposeAndNull(ref glyphAtlasSrv);
        }

        private static void DisposeAndNull<T>(ref T field) where T : class, IDisposable
        {
            if (field == null) return;
            field.Dispose();
            field = null;
        }

        private static float NormalizeAngle(float a)
        {
            while (a > Math.PI) a -= (float)(2.0 * Math.PI);
            while (a < -Math.PI) a += (float)(2.0 * Math.PI);
            return a;
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "UNK";
            var s = name.Trim().ToUpperInvariant();
            if (s.Length > 8) s = s.Substring(0, 8);
            return s;
        }

        private static string Sanitize(string text)
        {
            var chars = new List<char>();
            var u = text.ToUpperInvariant();
            for (int i = 0; i < u.Length && chars.Count < 28; i++)
            {
                char c = u[i];
                if (c >= 32 && c <= 127) chars.Add(c);
                else chars.Add(' ');
            }
            return new string(chars.ToArray());
        }

        private struct UvRect
        {
            public float U0, V0, U1, V1;
            public UvRect(float u0, float v0, float u1, float v1)
            {
                U0 = u0; V0 = v0; U1 = u1; V1 = v1;
            }
        }

        private struct TextVertex
        {
            public Vector3 Pos;
            public Vector2 Uv;
            public TextVertex(Vector3 p, Vector2 uv)
            {
                Pos = p;
                Uv = uv;
            }
        }

        private struct TrackState
        {
            public bool HasPrev;
            public float LastAz;
            public float DisplayAz;
            public DateTime LastUpdateUtc;
        }

        private struct ModelConstantBuffer
        {
            public Matrix4x4 model;
        }
    }
}
