using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using SharpDX.Direct2D1;
using HololensHermes.Navigation;

namespace HololensHermes.Services
{
    /// <summary>
    /// Loads floor-plan bitmap data and adapts the platform-neutral calibration
    /// transform to the SharpDX coordinates used by the HoloLens renderers.
    /// </summary>
    public sealed class FloorPlanService
    {
        /// <summary>
        /// Validates and decodes a floor-plan image URI. Texture creation remains
        /// device-owned and must happen in FloorPlanRenderer; returning null here
        /// deliberately prevents a decoded image from crossing the wrong Direct3D
        /// device boundary.
        /// </summary>
        public async Task<Bitmap1> LoadBitmapFromUriAsync(string uri, float desiredPixelWidth)
        {
            if (string.IsNullOrWhiteSpace(uri) || desiredPixelWidth <= 0.0f)
                return null;

            try
            {
                StorageFile file;
                if (uri.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
                {
                    file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
                }
                else
                {
                    file = await LoadFromNetworkAsync(uri);
                }

                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0)
                        return null;

                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)desiredPixelWidth,
                        ScaledHeight = (uint)(desiredPixelWidth * decoder.PixelHeight / decoder.PixelWidth)
                    };
                    await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    // The renderer creates Bitmap1 on its own Direct2D device.
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Task<StorageFile> LoadFromNetworkAsync(string uri)
        {
            // Network bytes must be transferred and converted by FloorPlanRenderer,
            // which owns the Direct3D device used to build the texture.
            throw new NotSupportedException("Floor-plan network texture creation is renderer-owned.");
        }

        /// <summary>
        /// Computes a least-squares similarity transform from 3+ image/world tap
        /// pairs. All pairs contribute to scale, rotation, and translation.
        /// </summary>
        public static AffineFloorPlanTransform ComputeTransform(Point[] imagePoints, Vector3[] worldPoints)
        {
            if (imagePoints == null) throw new ArgumentNullException("imagePoints");
            if (worldPoints == null) throw new ArgumentNullException("worldPoints");
            if (imagePoints.Length != worldPoints.Length || imagePoints.Length < 3)
                throw new ArgumentException("Need at least three matching point pairs.");

            var planPoints = new List<PlanPoint>(imagePoints.Length);
            var worldPlanPoints = new List<WorldPoint>(worldPoints.Length);
            for (var i = 0; i < imagePoints.Length; i++)
            {
                planPoints.Add(new PlanPoint(imagePoints[i].X, imagePoints[i].Y));
                worldPlanPoints.Add(new WorldPoint(worldPoints[i].X, worldPoints[i].Z));
            }

            var transform = FloorPlanTransform.Create(planPoints, worldPlanPoints);
            return new AffineFloorPlanTransform
            {
                Scale = (float)transform.Scale,
                RotationRadians = (float)transform.RotationRadians,
                Translation = new Vector3((float)transform.TranslationX, 0.0f, (float)transform.TranslationZ)
            };
        }
    }

    public sealed class AffineFloorPlanTransform
    {
        public float Scale { get; set; }
        public float RotationRadians { get; set; }
        public Vector3 Translation { get; set; }

        public Vector3 MapImagePointToWorld(Point imagePoint)
        {
            var cosine = (float)Math.Cos(RotationRadians);
            var sine = (float)Math.Sin(RotationRadians);
            var x = imagePoint.X;
            var y = imagePoint.Y;
            return new Vector3(
                Translation.X + Scale * (cosine * (float)x - sine * (float)y),
                0.0f,
                Translation.Z + Scale * (sine * (float)x + cosine * (float)y));
        }

        /// <summary>Maps a HoloLens world X/Z position into calibrated plan coordinates.</summary>
        public Point MapWorldPointToImage(Vector3 worldPoint)
        {
            if (Scale <= 0.0f)
                throw new InvalidOperationException("A positive floor-plan scale is required.");

            var dx = (worldPoint.X - Translation.X) / Scale;
            var dz = (worldPoint.Z - Translation.Z) / Scale;
            var cosine = (float)Math.Cos(RotationRadians);
            var sine = (float)Math.Sin(RotationRadians);
            return new Point(
                cosine * dx + sine * dz,
                -sine * dx + cosine * dz);
        }
    }
}
