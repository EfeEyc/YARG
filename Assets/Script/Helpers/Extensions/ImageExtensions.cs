using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Helpers.Extensions
{
    public static class ImageExtensions
    {
        private sealed class AlbumCoverRequestTracker : MonoBehaviour
        {
            private RawImage _rawImage;

            public void Initialize(RawImage rawImage)
            {
                _rawImage = rawImage;
            }

            private void OnDestroy()
            {
                if (_rawImage != null)
                    ClearRequest(_rawImage);
            }
        }

        public static Texture2D LoadTexture(this YARGImage image, bool mips)
        {
            var gfxFormat = image.Format switch
            {
                ImageFormat.RGB or ImageFormat.Grayscale => TextureFormat.RGB24,
                ImageFormat.RGBA or ImageFormat.GrayScale_Alpha => TextureFormat.RGBA32,
                ImageFormat.DXT1 => TextureFormat.DXT1,
                ImageFormat.DXT5 => TextureFormat.DXT5,
                _ => throw new ArgumentException("Unsupported image format"),
            };

            bool decodeDxtToRgba = (image.Format is ImageFormat.DXT1 or ImageFormat.DXT5) &&
                !SystemInfo.SupportsTextureFormat(gfxFormat);
            if (decodeDxtToRgba)
            {
                gfxFormat = TextureFormat.RGBA32;
            }

            var texture = new Texture2D(image.Width, image.Height, gfxFormat, mips);
            unsafe
            {
                var data = texture.GetPixelData<byte>(0);
                var ptr = (byte*)data.GetUnsafeReadOnlyPtr();
                switch (image.Format)
                {
                    case ImageFormat.Grayscale:
                        // Unity does not directly support Grayscale formats
                        new GrayscaleToRGBJob(image.Data, ptr)
                            .Schedule(image.Width * image.Height, 128)
                            .Complete();
                        break;
                    case ImageFormat.GrayScale_Alpha:
                        // Unity does not directly support Grayscale formats
                        new GrayscaleAlphaToRGBAJob(image.Data, ptr)
                            .Schedule(image.Width * image.Height, 128)
                            .Complete();
                        break;
                    case ImageFormat.DXT1:
                    case ImageFormat.DXT5:
                        if (decodeDxtToRgba)
                        {
                            DecodeDxtToRgba32(image.Data, ptr, image.Width, image.Height, image.Format == ImageFormat.DXT5);
                        }
                        else
                        {
                            for (int i = 0; i < data.Length; i += 2)
                            {
                                ptr[i] = image.Data[i + 1];
                                ptr[i + 1] = image.Data[i];
                            }
                        }
                        break;
                    default:
                        Unsafe.CopyBlock(ptr, image.Data, (uint) data.Length);
                        break;

                }
            }
            texture.Apply(true, true);
            return texture;
        }

        private static unsafe void DecodeDxtToRgba32(byte* src, byte* dst, int width, int height, bool isDxt5)
        {
            int blocksWide = (width + 3) / 4;
            int blocksHigh = (height + 3) / 4;
            int blockSize = isDxt5 ? 16 : 8;
            byte* blockPtr = src;

            Span<byte> colors = stackalloc byte[16];
            Span<byte> alphas = stackalloc byte[8];

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    byte* colorBlock = blockPtr;
                    if (isDxt5)
                    {
                        alphas[0] = blockPtr[1];
                        alphas[1] = blockPtr[0];
                        if (alphas[0] > alphas[1])
                        {
                            for (int i = 1; i < 7; i++)
                                alphas[i + 1] = (byte)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
                        }
                        else
                        {
                            for (int i = 1; i < 5; i++)
                                alphas[i + 1] = (byte)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
                            alphas[6] = 0;
                            alphas[7] = 255;
                        }
                        colorBlock = blockPtr + 8;
                    }

                    ushort c0 = (ushort)((colorBlock[0] << 8) | colorBlock[1]);
                    ushort c1 = (ushort)((colorBlock[2] << 8) | colorBlock[3]);

                    UnpackRgb565(c0, out colors[0], out colors[1], out colors[2]);
                    colors[3] = 255;
                    UnpackRgb565(c1, out colors[4], out colors[5], out colors[6]);
                    colors[7] = 255;

                    if (c0 > c1 || isDxt5)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            colors[8 + c] = (byte)((2 * colors[c] + colors[4 + c]) / 3);
                            colors[12 + c] = (byte)((colors[c] + 2 * colors[4 + c]) / 3);
                        }
                        colors[11] = 255;
                        colors[15] = 255;
                    }
                    else
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            colors[8 + c] = (byte)((colors[c] + colors[4 + c]) / 2);
                            colors[12 + c] = 0;
                        }
                        colors[11] = 255;
                        colors[15] = 0;
                    }

                    uint colorIndices = (uint)((colorBlock[5] << 0) | (colorBlock[4] << 8) | (colorBlock[7] << 16) | (colorBlock[6] << 24));
                    ulong alphaIndices = 0;
                    if (isDxt5)
                    {
                        alphaIndices =
                            ((ulong)blockPtr[3] << 0) |
                            ((ulong)blockPtr[2] << 8) |
                            ((ulong)blockPtr[5] << 16) |
                            ((ulong)blockPtr[4] << 24) |
                            ((ulong)blockPtr[7] << 32) |
                            ((ulong)blockPtr[6] << 40);
                    }

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= height) continue;
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= width) continue;

                            int pixelIdx = py * 4 + px;
                            int cIdx = (int)((colorIndices >> (pixelIdx * 2)) & 0x3) * 4;
                            int dstIdx = (y * width + x) * 4;

                            dst[dstIdx + 0] = colors[cIdx + 0];
                            dst[dstIdx + 1] = colors[cIdx + 1];
                            dst[dstIdx + 2] = colors[cIdx + 2];
                            dst[dstIdx + 3] = isDxt5
                                ? alphas[(int)((alphaIndices >> (pixelIdx * 3)) & 0x7)]
                                : colors[cIdx + 3];
                        }
                    }

                    blockPtr += blockSize;
                }
            }
        }

        private static void UnpackRgb565(ushort c, out byte r, out byte g, out byte b)
        {
            int r5 = (c >> 11) & 0x1F;
            int g6 = (c >> 5) & 0x3F;
            int b5 = c & 0x1F;
            r = (byte)((r5 << 3) | (r5 >> 2));
            g = (byte)((g6 << 2) | (g6 >> 4));
            b = (byte)((b5 << 3) | (b5 >> 2));
        }

        private sealed class AlbumCoverRequestState
        {
            public int RequestId;
            public SongEntry SongEntry;
        }

        private static readonly Dictionary<int, AlbumCoverRequestState> _currentByImage = new();

        private static void ClearRequest(RawImage rawImage)
        {
            if (rawImage == null) return;

            _currentByImage.Remove(rawImage.GetInstanceID());
        }

        public static async void LoadAlbumCover(this RawImage rawImage, SongEntry songEntry, CancellationToken cancellationToken, float alpha = 1)
        {
            if (rawImage == null) return;

            var tracker = rawImage.GetComponent<AlbumCoverRequestTracker>();
            if (tracker == null)
                tracker = rawImage.gameObject.AddComponent<AlbumCoverRequestTracker>();
            
            tracker.Initialize(rawImage);

            var instanceId = rawImage.GetInstanceID();
            if (!_currentByImage.TryGetValue(instanceId, out var state))
            {
                state = new AlbumCoverRequestState();
                _currentByImage[instanceId] = state;
            }

            state.RequestId++;
            state.SongEntry = songEntry;
            var requestId = state.RequestId;
            using var image = await UniTask.RunOnThreadPool(songEntry.LoadAlbumData);
            // Everything that happens after this conditional should only happen *once*
            // There's no reason to create and destroy texture objects if they're just gonna be overriden
            if (!_currentByImage.TryGetValue(instanceId, out state) || state.RequestId != requestId || state.SongEntry != songEntry)
            {
                return;
            }

            lock (rawImage)
            {
                // Dispose of the old texture (prevent memory leaks)
                UnityEngine.Object.Destroy(rawImage.texture);

                if (cancellationToken.IsCancellationRequested) return;

                if (image != null)
                {
                    rawImage.texture = image.LoadTexture(false);
                    rawImage.uvRect = new Rect(0f, 0f, 1f, -1f);
                    rawImage.color = Color.white.WithAlpha(alpha);
                }
                else
                {
                    rawImage.texture = null;
                    rawImage.color = Color.clear;
                }
            }
        }

        [BurstCompile]
        private readonly unsafe struct GrayscaleToRGBJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            private readonly byte* _originalTexture;
            [NativeDisableUnsafePtrRestriction]
            private readonly byte* _resultTexture;

            public GrayscaleToRGBJob(byte* originalTexture, byte* resultTexture)
            {
                _originalTexture = originalTexture;
                _resultTexture = resultTexture;
            }

            public readonly void Execute(int index)
            {
                const int PIXELSIZE = 3;
                int resultIndex = PIXELSIZE * index;

                var value = _originalTexture[index];

                _resultTexture[resultIndex] = value;
                _resultTexture[resultIndex + 1] = value;
                _resultTexture[resultIndex + 2] = value;
            }
        }

        [BurstCompile]
        private readonly unsafe struct GrayscaleAlphaToRGBAJob : IJobParallelFor
        {
            [NativeDisableUnsafePtrRestriction]
            private readonly byte* _originalTexture;
            [NativeDisableUnsafePtrRestriction]
            private readonly byte* _resultTexture;

            public GrayscaleAlphaToRGBAJob(byte* originalTexture, byte* resultTexture)
            {
                _originalTexture = originalTexture;
                _resultTexture = resultTexture;
            }

            public readonly void Execute(int index)
            {
                int originalIndex = index << 1;
                int resultIndex = index << 2;

                byte value = _originalTexture[originalIndex];
                byte alpha = _originalTexture[originalIndex + 1];

                _resultTexture[resultIndex] = value;
                _resultTexture[resultIndex + 1] = value;
                _resultTexture[resultIndex + 2] = value;
                _resultTexture[resultIndex + 3] = alpha;
            }
        }
    }
}
