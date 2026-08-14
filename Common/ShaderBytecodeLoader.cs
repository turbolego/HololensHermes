using System;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Storage;

namespace HololensHermes.Common
{
    /// <summary>
    /// Loads shader bytecode compiled by the FxCompile target from the deployed
    /// app package. The HLSL compiler emits .cso assets; it does not generate
    /// C# classes, so renderers must load those assets explicitly.
    /// </summary>
    internal static class ShaderBytecodeLoader
    {
        public static async Task<byte[]> LoadAsync(string packageRelativePath)
        {
            if (string.IsNullOrWhiteSpace(packageRelativePath))
                throw new ArgumentException("A shader package path is required.", "packageRelativePath");

            var file = await StorageFile.GetFileFromApplicationUriAsync(
                new Uri("ms-appx:///" + packageRelativePath.TrimStart('/')));
            var buffer = await FileIO.ReadBufferAsync(file);
            byte[] bytes;
            CryptographicBuffer.CopyToByteArray(buffer, out bytes);
            return bytes;
        }
    }
}
