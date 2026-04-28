using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using HololensSatelliteViewer.Common;
using HololensSatelliteViewer.Models;
using HololensSatelliteViewer.Services;
using Windows.UI.Input.Spatial;

namespace HololensSatelliteViewer.Content
{
    internal class SatelliteRenderer : Disposer
    {
        private readonly DeviceResources deviceResources;
        private SharpDX.Direct3D11.InputLayout inputLayout;
        private SharpDX.Direct3D11.Buffer vertexBuffer;
        private SharpDX.Direct3D11.Buffer indexBuffer;
        private SharpDX.Direct3D11.VertexShader vertexShader;
        private SharpDX.Direct3D11.GeometryShader geometryShader;
        private SharpDX.Direct3D11.PixelShader pixelShader;
        private SharpDX.Direct3D11.Buffer modelConstantBuffer;
        private ModelConstantBuffer modelConstantBufferData;
        private int indexCount;
        private bool loadingComplete;
        private bool usingVprtShaders;

        private readonly OrbitService orbitService;
        private readonly GeolocationService geolocationService;
        private bool fetchInProgress;
        private DateTime lastFetchUtc = DateTime.MinValue;
        private volatile List<Satellite> satellites = new List<Satellite>();

        private Vector3 currentHeadPosition = Vector3.Zero;
        private Vector3 worldCenter = Vector3.Zero;
        private bool worldCenterLocked;
        private float ceilingY;

        private string gpsDebug = "GPS: --";

        private const int MaxSatellitesRendered = 10;
        private const float SatCubeScale = 0.14f;
        private const float DomeRadiusMeters = 1.8f;
        private const float CeilingOffset = 1.4f;
        private const float CeilingClearance = 0.3f;

        private readonly Dictionary<int, TrackState> tracks = new Dictionary<int, TrackState>();

        private static readonly Dictionary<char, ushort> Glyphs = new Dictionary<char, ushort>
        {
            {'A', 0b_010_101_111_101_101}, {'B', 0b_110_101_110_101_110}, {'C', 0b_011_100_100_100_011},
            {'D', 0b_110_101_101_101_110}, {'E', 0b_111_100_110_100_111}, {'F', 0b_111_100_110_100_100},
            {'G', 0b_011_100_101_101_011}, {'H', 0b_101_101_111_101_101}, {'I', 0b_111_010_010_010_111},
            {'J', 0b_001_001_001_101_010}, {'K', 0b_101_101_110_101_101}, {'L', 0b_100_100_100_100_111},
            {'M', 0b_101_111_101_101_101}, {'N', 0b_101_111_111_111_101}, {'O', 0b_010_101_101_101_010},
            {'P', 0b_110_101_110_100_100}, {'Q', 0b_010_101_101_111_011}, {'R', 0b_110_101_110_110_101},
            {'S', 0b_011_100_010_001_110}, {'T', 0b_111_010_010_010_010}, {'U', 0b_101_101_101_101_111},
            {'V', 0b_101_101_101_101_010}, {'W', 0b_101_101_111_111_101}, {'X', 0b_101_101_010_101_101},
            {'Y', 0b_101_101_010_010_010}, {'Z', 0b_111_001_010_100_111},
            {'0', 0b_111_101_101_101_111}, {'1', 0b_010_110_010_010_111}, {'2', 0b_111_001_111_100_111},
            {'3', 0b_111_001_111_001_111}, {'4', 0b_101_101_111_001_001}, {'5', 0b_111_100_111_001_111},
            {'6', 0b_111_100_111_101_111}, {'7', 0b_111_001_010_010_010}, {'8', 0b_111_101_111_101_111},
            {'9', 0b_111_101_111_001_111}, {'-', 0b_000_000_111_000_000}, {'_', 0b_000_000_000_000_111},
            {'.', 0b_000_000_000_000_010}, {':', 0b_000_010_000_010_000}, {' ', 0}
        };

        public SatelliteRenderer(DeviceResources deviceResources)
        {
            this.deviceResources = deviceResources;
            this.orbitService = new OrbitService();
            this.geolocationService = new GeolocationService();
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
                    var closest = live
                        .Where(s => s.Elevation > 0.0)
                        .OrderBy(s => s.RangeKm)
                        .Take(MaxSatellitesRendered)
                        .ToList();

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

            var context = deviceResources.D3DDeviceContext;

            int stride = SharpDX.Utilities.SizeOf<VertexPositionColor>();
            context.InputAssembler.SetVertexBuffers(0, new SharpDX.Direct3D11.VertexBufferBinding(vertexBuffer, stride, 0));
            context.InputAssembler.SetIndexBuffer(indexBuffer, SharpDX.DXGI.Format.R16_UInt, 0);
            context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.InputAssembler.InputLayout = inputLayout;
            context.VertexShader.SetShader(vertexShader, null, 0);
            context.VertexShader.SetConstantBuffers(0, modelConstantBuffer);
            if (!usingVprtShaders)
            {
                context.GeometryShader.SetShader(geometryShader, null, 0);
            }
            context.PixelShader.SetShader(pixelShader, null, 0);

            var snapshot = satellites;
            var debugLines = new List<string>();
            debugLines.Add(gpsDebug);

            foreach (var sat in snapshot)
            {
                Vector3 pos = ComputeSatellitePosition(sat);
                DrawCubeAt(pos, SatCubeScale);

                debugLines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1:F2},{2:F2}",
                    ShortName(sat.Name),
                    sat.Latitude,
                    sat.Longitude));
            }

            DrawDebugWindow(debugLines);
        }

        public Vector3 Position => worldCenter;

        private Vector3 ComputeSatellitePosition(Satellite sat)
        {
            double az = sat.Azimuth * Math.PI / 180.0;
            double el = sat.Elevation * Math.PI / 180.0;

            int key = sat.NoradId > 0 ? sat.NoradId : sat.Name.GetHashCode();
            TrackState state;
            if (!tracks.TryGetValue(key, out state))
            {
                state = new TrackState
                {
                    HasPrev = false,
                    DisplayAz = (float)az,
                    LastUpdateUtc = DateTime.UtcNow
                };
            }

            float dt = (float)(DateTime.UtcNow - state.LastUpdateUtc).TotalSeconds;
            if (dt < 0.0001f)
            {
                dt = 0.0001f;
            }

            float targetAz = (float)az;
            if (state.HasPrev)
            {
                float deltaAz = NormalizeAngle(targetAz - state.LastAz);
                // motion amplification to make direction clearly visible
                state.DisplayAz += deltaAz * 2.2f;
            }
            else
            {
                state.DisplayAz = targetAz;
            }

            state.LastAz = targetAz;
            state.LastUpdateUtc = DateTime.UtcNow;
            state.HasPrev = true;
            tracks[key] = state;

            float horizontal = DomeRadiusMeters * (float)Math.Max(0.15, Math.Cos(el));
            float x = (float)Math.Sin(state.DisplayAz) * horizontal;
            float z = (float)(-Math.Cos(state.DisplayAz)) * horizontal;

            // Keep satellites near ceiling, 30cm below.
            float y = (ceilingY - CeilingClearance) + (float)Math.Sin(el) * 0.25f;

            // Only above local horizon and above floor relative to center.
            if (sat.Elevation <= 0.0 || y < worldCenter.Y - 0.1f)
            {
                y = worldCenter.Y - 0.1f;
            }

            return new Vector3(worldCenter.X + x, y, worldCenter.Z + z);
        }

        private void DrawDebugWindow(List<string> lines)
        {
            Vector3 panelCenter = worldCenter + new Vector3(0.0f, 0.15f, -1.15f);

            // Draw simple frame
            DrawRect(panelCenter, 1.6f, 0.95f, 0.05f);

            float startY = panelCenter.Y + 0.38f;
            for (int i = 0; i < lines.Count && i < 11; i++)
            {
                DrawTextLine(lines[i], new Vector3(panelCenter.X - 0.72f, startY - i * 0.075f, panelCenter.Z - 0.01f));
            }
        }

        private void DrawRect(Vector3 center, float width, float height, float thickness)
        {
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;

            DrawCubeAt(center + new Vector3(0, halfH, 0), thickness * 0.8f); // top center mark
            for (int i = 0; i <= 16; i++)
            {
                float t = -halfW + i * (width / 16f);
                DrawCubeAt(center + new Vector3(t, halfH, 0), thickness);
                DrawCubeAt(center + new Vector3(t, -halfH, 0), thickness);
            }
            for (int i = 0; i <= 10; i++)
            {
                float t = -halfH + i * (height / 10f);
                DrawCubeAt(center + new Vector3(-halfW, t, 0), thickness);
                DrawCubeAt(center + new Vector3(halfW, t, 0), thickness);
            }
        }

        private void DrawTextLine(string text, Vector3 start)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string clean = Sanitize(text);
            const float dotStep = 0.012f;
            const float charStep = 0.048f;

            for (int ci = 0; ci < clean.Length; ci++)
            {
                ushort glyph;
                if (!Glyphs.TryGetValue(clean[ci], out glyph))
                {
                    glyph = Glyphs[' '];
                }

                for (int row = 0; row < 5; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int bit = row * 3 + col;
                        if (((glyph >> (14 - bit)) & 1) == 0)
                        {
                            continue;
                        }

                        Vector3 p = new Vector3(
                            start.X + ci * charStep + col * dotStep,
                            start.Y - row * dotStep,
                            start.Z);

                        DrawCubeAt(p, 0.055f);
                    }
                }
            }
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "UNK";
            }
            var s = name.Trim().ToUpperInvariant();
            if (s.Length > 8)
            {
                s = s.Substring(0, 8);
            }
            return s;
        }

        private static string Sanitize(string text)
        {
            var chars = new List<char>();
            var upper = text.ToUpperInvariant();
            for (int i = 0; i < upper.Length && chars.Count < 26; i++)
            {
                char c = upper[i];
                if (Glyphs.ContainsKey(c))
                {
                    chars.Add(c);
                }
                else if (c == ',')
                {
                    chars.Add('.');
                }
                else
                {
                    chars.Add(' ');
                }
            }
            return new string(chars.ToArray());
        }

        private static float NormalizeAngle(float a)
        {
            while (a > Math.PI) a -= (float)(2.0 * Math.PI);
            while (a < -Math.PI) a += (float)(2.0 * Math.PI);
            return a;
        }

        private void DrawCubeAt(Vector3 worldPos, float scale)
        {
            Matrix4x4 m = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(worldPos);
            modelConstantBufferData.model = Matrix4x4.Transpose(m);
            deviceResources.D3DDeviceContext.UpdateSubresource(ref modelConstantBufferData, modelConstantBuffer);
            deviceResources.D3DDeviceContext.DrawIndexedInstanced(indexCount, 2, 0, 0, 0);
        }

        public async void CreateDeviceDependentResourcesAsync()
        {
            ReleaseDeviceDependentResources();

            usingVprtShaders = deviceResources.D3DDeviceSupportsVprt;
            var folder = Windows.ApplicationModel.Package.Current.InstalledLocation;

            string vsFile = usingVprtShaders ? "Content\\Shaders\\VPRTVertexShader.cso" : "Content\\Shaders\\VertexShader.cso";
            var vsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync(vsFile));
            vertexShader = ToDispose(new SharpDX.Direct3D11.VertexShader(deviceResources.D3DDevice, vsBytes));

            SharpDX.Direct3D11.InputElement[] vertexDesc =
            {
                new SharpDX.Direct3D11.InputElement("POSITION", 0, SharpDX.DXGI.Format.R32G32B32_Float, 0, 0, SharpDX.Direct3D11.InputClassification.PerVertexData, 0),
                new SharpDX.Direct3D11.InputElement("COLOR", 0, SharpDX.DXGI.Format.R32G32B32_Float, 12, 0, SharpDX.Direct3D11.InputClassification.PerVertexData, 0),
            };
            inputLayout = ToDispose(new SharpDX.Direct3D11.InputLayout(deviceResources.D3DDevice, vsBytes, vertexDesc));

            if (!usingVprtShaders)
            {
                var gsBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync("Content\\Shaders\\GeometryShader.cso"));
                geometryShader = ToDispose(new SharpDX.Direct3D11.GeometryShader(deviceResources.D3DDevice, gsBytes));
            }

            var psBytes = await DirectXHelper.ReadDataAsync(await folder.GetFileAsync("Content\\Shaders\\PixelShader.cso"));
            pixelShader = ToDispose(new SharpDX.Direct3D11.PixelShader(deviceResources.D3DDevice, psBytes));

            VertexPositionColor[] verts =
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
            vertexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(deviceResources.D3DDevice, SharpDX.Direct3D11.BindFlags.VertexBuffer, verts));

            ushort[] idx =
            {
                2,1,0, 2,3,1,
                6,4,5, 6,5,7,
                0,1,5, 0,5,4,
                2,6,7, 2,7,3,
                0,4,6, 0,6,2,
                1,3,7, 1,7,5,
            };
            indexCount = idx.Length;
            indexBuffer = ToDispose(SharpDX.Direct3D11.Buffer.Create(deviceResources.D3DDevice, SharpDX.Direct3D11.BindFlags.IndexBuffer, idx));

            modelConstantBuffer = ToDispose(new SharpDX.Direct3D11.Buffer(
                deviceResources.D3DDevice,
                SharpDX.Utilities.SizeOf<ModelConstantBuffer>(),
                SharpDX.Direct3D11.ResourceUsage.Default,
                SharpDX.Direct3D11.BindFlags.ConstantBuffer,
                SharpDX.Direct3D11.CpuAccessFlags.None,
                SharpDX.Direct3D11.ResourceOptionFlags.None,
                0));

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
        }

        private static void DisposeAndNull<T>(ref T field) where T : class, IDisposable
        {
            if (field == null) return;
            field.Dispose();
            field = null;
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
