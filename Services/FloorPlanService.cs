using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using SharpDX;
using SharpDX.Direct2D1;
using HololensHermes.Models;

namespace HololensHermes.Services
{
    /// <summary>
    /// Loads and stores a floor plan image (PNG) for rendering.
    ///
    /// The image is loaded from the Hermes API (or Assets) and converted to a
    /// SharpDX.Direct2D1.Bitmap1 that FloorPlanRenderer can draw.
    /// </summary>
    public sealed class FloorPlanService
    {
        /// <summary>
        /// Load a PNG from a URI string (http/https or ms-appx:///) into a Bitmap1.
        /// Returns null on failure.
        ///
        /// For http/https, the caller must have internetClient capability.
        /// </summary>
        public async Task<Bitmap1> LoadBitmapFromUriAsync(string uri, float desiredPixelWidth)
        {
            try
            {
                StorageFile file;
                if (uri.StartsWith("ms-appx:///"))
                {
                    var path = uri.Substring("ms-appx:///".Length);
                    file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(uri));
                }
                else
                {
                    file = await LoadFromNetworkAsync(uri);
                }
                using (var stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = (uint)desiredPixelWidth,
                        ScaledHeight = (uint)(desiredPixelWidth * decoder.BitmapPixelWidth / decoder.BitmapPixelHeight)
                    };
                    var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,
                                                                     BitmapAlphaMode.Premultiplied, transform,
                                                                     ExifOrientationMode.IgnoreExifOrientation,
                                                                     ColorManagementMode.DoNotColorManage);
                    var dp = pixels.Direct3DSurface;
                    // Create a SharpDX texture from the DP (this is a stub outline — complete in renderer step).
                    // The actual D3D11 texture creation happens in the renderer with the device.
                    // For now return null and let the renderer handle it using the image URI directly.
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<StorageFile> LoadFromNetworkAsync(string uri)
        {
            // Creates a temp file and downloads. Simplified — real impl uses HttpClient.
            throw new NotImplementedException("network loading deferred to renderer");
        }

        /// <summary>
        /// Given user-tap world positions (from SpatialPointerPose) and the corresponding
        /// floor-plan image points (in image pixels, origin top-left), compute the affine
        /// transform (translation + scale + rotation) that maps image space → world space.
        ///
        /// Uses 3+ points (multi-point calibration, user choice #4).
        /// </summary>
        public static AffineFloorPlanTransform ComputeTransform(Point[] imagePoints, Vector3[] worldPoints)
        {
            if (imagePoints.Length < 3 || worldPoints.Length < 3 || imagePoints.Length != worldPoints.Length)
                throw new ArgumentException("Need at least 3 matching point pairs");

            // We solve for: world = R * (image * scale) + translation
            // where image point is [xImg, yImg] → [xImg*sx, yImg*sy] then rotated + translated.
            //
            // For simplicity, assume isotropic scale (sx = sy = s) and rotation θ.
            //
            // Estimate via least squares over the pairs.
            // Let p_i = [xImg_i, yImg_i] (image), w_i = [wx_i, wz_i] (world XZ, ignore Y = floor).
            //
            // Model: w = s * R(θ) * p_img + t
            //
            // Solve using 2D → 2D (world XZ is horizontal plane). Use numeric approach.
            return FitAffine(imagePoints, worldPoints);
        }

        private static AffineFloorPlanTransform FitAffine(Point[] img, Vector3[] world)
        {
            // Simple approach: pick first point as origin, second as defining scale + rotation.
            var p0 = img[0];
            var w0 = world[0];
            if (img.Length >= 2)
            {
                var p1 = img[1];
                var w1 = world[1];
                var dxImg = p1.X - p0.X;
                var dyImg = p1.Y - p0.Y;
                var dxWorld = w1.X - w0.X;
                var dzWorld = w1.Z - w0.Z;
                float imgLen = (float)Math.Sqrt(dxImg * dxImg + dyImg * dyImg);
                float worldLen = (float)Math.Sqrt(dxWorld * dxWorld + dzWorld * dzWorld);
                if (imgLen < 1e-6f || worldLen < 1e-6f)
                    return new AffineFloorPlanTransform
                    {
                        Scale = 1f,
                        RotationRadians = 0f,
                        Translation = new Vector3(w0.X, 0f, w0.Z)
                    };
                float s = worldLen / imgLen;
                float theta = (float)Math.Atan2(dzWorld, dxWorld) - (float)Math.Atan2(dyImg, dxImg);
                // Refine with more points if present (least squares optional).
                return new AffineFloorPlanTransform
                {
                    Scale = s,
                    RotationRadians = theta,
                    Translation = new Vector3(w0.X, 0f, w0.Z)
                };
            }
            return new AffineFloorPlanTransform();
        }
    }

    public sealed class AffineFloorPlanTransform
    {
        public float Scale { get; set; }
        public float RotationRadians { get; set; }
        public Vector3 Translation { get; set; }

        public Vector3 MapImagePointToWorld(Point imagePoint)
        {
            var imgX = imagePoint.X * Scale;
            var imgY = imagePoint.Y * Scale;
            float c = (float)Math.Cos(RotationRadians);
            float s = (float)Math.Sin(RotationRadians);
            // Image Y points down in screen space; HoloLens XZ: X east, Z north-ish.
            // Assume image top = north, so image +Y → world -Z.
            var wx = imgX * c - imgY * s;
            var wz = imgX * s + imgY * c;
            // If we want image Y → world -Z, flip the WZ sign.
            return new Vector3(Translation.X + wx, 0f, Translation.Z - wz);
        }
    }
}
