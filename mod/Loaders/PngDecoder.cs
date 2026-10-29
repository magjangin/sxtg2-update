using System;
using System.IO;
using System.IO.Compression;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Minimal managed PNG decoder so jackets can be decoded off the main thread. The game build strips Unity's async
/// texture path (DownloadHandlerTexture's constructor: "Method not found"), and ImageConversion.LoadImage must run
/// on the main thread (~50-100 ms per 1080p PNG).
/// Supports 8-bit, non-interlaced PNGs of color type 0/2/3/4/6 (gray, RGB, palette (+tRNS), gray+alpha, RGBA).
/// Output is RGBA32 with rows bottom-up (Unity texture origin), optionally box-downscaled to maxWidth.
/// Anything else returns false and the caller falls back to LoadImage.
/// </summary>
public static class PngDecoder
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static bool TryDecode(byte[] file, int maxWidth, out int width, out int height, out byte[] rgba)
    {
        width = height = 0;
        rgba = Array.Empty<byte>();
        if (file.Length < 33) return false;
        for (int i = 0; i < 8; i++) if (file[i] != Signature[i]) return false;

        int w = 0, h = 0, colorType = 0;
        byte[]? palette = null, paletteAlpha = null;
        using var idat = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= file.Length)
        {
            int len = ReadInt(file, pos);
            string type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
            int data = pos + 8;
            if (len < 0 || data + len > file.Length) return false;

            switch (type)
            {
                case "IHDR":
                    w = ReadInt(file, data);
                    h = ReadInt(file, data + 4);
                    int bitDepth = file[data + 8];
                    colorType = file[data + 9];
                    int interlace = file[data + 12];
                    if (bitDepth != 8 || interlace != 0) return false;
                    if (colorType != 0 && colorType != 2 && colorType != 3 && colorType != 4 && colorType != 6) return false;
                    break;
                case "PLTE":
                    palette = new byte[len];
                    Buffer.BlockCopy(file, data, palette, 0, len);
                    break;
                case "tRNS":
                    paletteAlpha = new byte[len];
                    Buffer.BlockCopy(file, data, paletteAlpha, 0, len);
                    break;
                case "IDAT":
                    idat.Write(file, data, len);
                    break;
            }
            if (type == "IEND") break;
            pos = data + len + 4; // skip CRC
        }
        if (w <= 0 || h <= 0 || (colorType == 3 && palette == null)) return false;

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int stride = w * channels;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = z.Read(raw, read, raw.Length - read);
                if (n <= 0) return false;
                read += n;
            }
        }

        // Unfilter in place (filter byte + scanline per row).
        var cur = new byte[stride];
        var prev = new byte[stride];
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int rowStart = y * (stride + 1);
            int filter = raw[rowStart];
            Buffer.BlockCopy(raw, rowStart + 1, cur, 0, stride);
            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? cur[x - channels] : 0;
                int b = prev[x];
                int c = x >= channels ? prev[x - channels] : 0;
                int v = cur[x];
                switch (filter)
                {
                    case 1: v += a; break;
                    case 2: v += b; break;
                    case 3: v += (a + b) >> 1; break;
                    case 4:
                        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                        break;
                }
                cur[x] = (byte)v;
            }

            // Expand to RGBA, writing rows bottom-up.
            int o = (h - 1 - y) * w * 4;
            for (int x = 0; x < w; x++, o += 4)
            {
                switch (colorType)
                {
                    case 0:
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = cur[x]; pixels[o + 3] = 255; break;
                    case 2:
                        pixels[o] = cur[x * 3]; pixels[o + 1] = cur[x * 3 + 1]; pixels[o + 2] = cur[x * 3 + 2]; pixels[o + 3] = 255; break;
                    case 3:
                        int idx = cur[x];
                        if (idx * 3 + 2 >= palette!.Length) return false;
                        pixels[o] = palette[idx * 3]; pixels[o + 1] = palette[idx * 3 + 1]; pixels[o + 2] = palette[idx * 3 + 2];
                        pixels[o + 3] = paletteAlpha != null && idx < paletteAlpha.Length ? paletteAlpha[idx] : (byte)255;
                        break;
                    case 4:
                        pixels[o] = pixels[o + 1] = pixels[o + 2] = cur[x * 2]; pixels[o + 3] = cur[x * 2 + 1]; break;
                    default:
                        Buffer.BlockCopy(cur, x * 4, pixels, o, 4); break;
                }
            }
            (prev, cur) = (cur, prev);
        }

        // Halve with a 2x2 box filter until it fits; the largest on-screen jacket is far smaller than 1080p sources.
        while (maxWidth > 0 && w > maxWidth && w >= 2 && h >= 2)
        {
            int nw = w / 2, nh = h / 2;
            var half = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int s0 = ((y * 2) * w + x * 2) * 4, s1 = s0 + w * 4, d = (y * nw + x) * 4;
                for (int k = 0; k < 4; k++)
                    half[d + k] = (byte)((pixels[s0 + k] + pixels[s0 + 4 + k] + pixels[s1 + k] + pixels[s1 + 4 + k] + 2) >> 2);
            }
            pixels = half;
            w = nw;
            h = nh;
        }

        width = w;
        height = h;
        rgba = pixels;
        return true;
    }

    private static int ReadInt(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
}
