using System.IO.Compression;
using SixtarGateMod.Loaders;
using Xunit;

namespace SixtarGateMod.Tests;

public class PngDecoderTests
{
    // PngDecoder는 CRC를 검사하지 않으므로 시험용 PNG는 CRC를 0으로 채운다.
    private static byte[] Chunk(string type, byte[] data)
    {
        var ms = new MemoryStream();
        void Be(int v) { ms.WriteByte((byte)(v >> 24)); ms.WriteByte((byte)(v >> 16)); ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        Be(data.Length);
        ms.Write(System.Text.Encoding.ASCII.GetBytes(type));
        ms.Write(data);
        Be(0);
        return ms.ToArray();
    }

    private static byte[] Png(int w, int h, int colorType, byte[] rawScanlines, int bitDepth = 8, int interlace = 0,
        byte[]? plte = null, byte[]? trns = null, bool truncate = false)
    {
        var ihdr = new byte[13];
        void Be(int o, int v) { ihdr[o] = (byte)(v >> 24); ihdr[o + 1] = (byte)(v >> 16); ihdr[o + 2] = (byte)(v >> 8); ihdr[o + 3] = (byte)v; }
        Be(0, w); Be(4, h);
        ihdr[8] = (byte)bitDepth; ihdr[9] = (byte)colorType; ihdr[12] = (byte)interlace;

        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) zs.Write(rawScanlines);
        byte[] idat = z.ToArray();
        if (truncate) idat = idat[..(idat.Length / 2)];

        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        png.Write(Chunk("IHDR", ihdr));
        if (plte != null) png.Write(Chunk("PLTE", plte));
        if (trns != null) png.Write(Chunk("tRNS", trns));
        png.Write(Chunk("IDAT", idat));
        png.Write(Chunk("IEND", Array.Empty<byte>()));
        return png.ToArray();
    }

    [Fact]
    public void Rgb_NoFilter_OutputsRowsBottomUpWithOpaqueAlpha()
    {
        // 위 행: 빨강, 초록 / 아래 행: 파랑, 흰색 (각 행 앞에 필터 바이트 0)
        var raw = new byte[]
        {
            0, 255, 0, 0,   0, 255, 0,
            0, 0, 0, 255,   255, 255, 255,
        };
        Assert.True(PngDecoder.TryDecode(Png(2, 2, 2, raw), 0, out int w, out int h, out var rgba));
        Assert.Equal((2, 2), (w, h));
        // Unity 텍스처는 맨 아래 행부터: 먼저 파랑, 흰색, 그다음 빨강, 초록
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 255, 255, 255, 255, 0, 0, 255, 0, 255, 0, 255 }, rgba);
    }

    [Fact]
    public void Rgba_SubFilter_AddsLeftPixel()
    {
        // 한 행, 필터 1(Sub): 두 번째 픽셀은 왼쪽 값에 더해진다
        var raw = new byte[] { 1, 10, 20, 30, 40, 1, 1, 1, 1 };
        Assert.True(PngDecoder.TryDecode(Png(2, 1, 6, raw), 0, out _, out _, out var rgba));
        Assert.Equal(new byte[] { 10, 20, 30, 40, 11, 21, 31, 41 }, rgba);
    }

    [Fact]
    public void Gray_UpFilter_AddsRowAbove()
    {
        var raw = new byte[] { 0, 100, 0, 2, 5, 5 }; // 2x2 회색조, 둘째 행은 Up 필터
        Assert.True(PngDecoder.TryDecode(Png(2, 2, 0, raw), 0, out _, out _, out var rgba));
        // 맨 아래(둘째) 행 먼저: 105, 5
        Assert.Equal(105, rgba[0]);
        Assert.Equal(5, rgba[4]);
    }

    [Fact]
    public void Palette_WithTrns_UsesPaletteAlpha()
    {
        var plte = new byte[] { 255, 0, 0, 0, 255, 0 };
        var trns = new byte[] { 128 }; // 0번만 반투명, 1번은 불투명(기본)
        var raw = new byte[] { 0, 0, 1 };
        Assert.True(PngDecoder.TryDecode(Png(2, 1, 3, raw, plte: plte, trns: trns), 0, out _, out _, out var rgba));
        Assert.Equal(new byte[] { 255, 0, 0, 128, 0, 255, 0, 255 }, rgba);
    }

    [Fact]
    public void Downscale_HalvesWithBoxFilterUntilItFits()
    {
        // 4x4 회색조: 왼쪽 두 열 0, 오른쪽 두 열 200 → 2x2는 0, 200
        var raw = new List<byte>();
        for (int y = 0; y < 4; y++) { raw.Add(0); raw.AddRange(new byte[] { 0, 0, 200, 200 }); }
        Assert.True(PngDecoder.TryDecode(Png(4, 4, 0, raw.ToArray()), 2, out int w, out int h, out var rgba));
        Assert.Equal((2, 2), (w, h));
        Assert.Equal(0, rgba[0]);
        Assert.Equal(200, rgba[4]);
    }

    [Fact]
    public void Interlaced_And16Bit_AreRejectedForFallback()
    {
        Assert.False(PngDecoder.TryDecode(Png(1, 1, 2, new byte[] { 0, 1, 2, 3 }, interlace: 1), 0, out _, out _, out _));
        Assert.False(PngDecoder.TryDecode(Png(1, 1, 0, new byte[] { 0, 1, 2 }, bitDepth: 16), 0, out _, out _, out _));
    }

    [Fact]
    public void TruncatedImageData_ReturnsFalse()
    {
        var big = new List<byte>();
        for (int y = 0; y < 64; y++) { big.Add(0); big.AddRange(Enumerable.Range(0, 64 * 3).Select(i => (byte)(i * 7 + y))); }
        Assert.False(PngDecoder.TryDecode(Png(64, 64, 2, big.ToArray(), truncate: true), 0, out _, out _, out _));
    }

    [Fact]
    public void HugeDeclaredSize_IsRejectedWithoutAllocating()
    {
        // 헤더만 큰 PNG: 상한(MaxPixels) 때문에 해독 버퍼를 잡기 전에 false
        Assert.False(PngDecoder.TryDecode(Png(7000, 7000, 2, new byte[] { 0, 0, 0, 0 }), 0, out _, out _, out _));
        Assert.False(PngDecoder.TryDecode(Png(20000, 1, 2, new byte[] { 0, 0, 0, 0 }), 0, out _, out _, out _));
    }

    [Fact]
    public void NotAPng_ReturnsFalse()
        => Assert.False(PngDecoder.TryDecode(new byte[64], 0, out _, out _, out _));

    [Fact]
    public void CorruptedZlibStream_ReturnsFalseWithoutThrowing()
    {
        var ihdr = new byte[13];
        ihdr[3] = 2; ihdr[7] = 2; ihdr[8] = 8; ihdr[9] = 6;
        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        png.Write(Chunk("IHDR", ihdr));
        png.Write(Chunk("IDAT", new byte[] { 0xFF, 0x00, 0xDE, 0xAD, 0xBE, 0xEF }));
        png.Write(Chunk("IEND", Array.Empty<byte>()));
        Assert.False(PngDecoder.TryDecode(png.ToArray(), 0, out _, out _, out _));
    }

    [Fact]
    public void AverageAndPaethFilters_DecodeCorrectly()
    {
        // 2x2 회색조: 0행 [10, 20] (None), 1행 [30, 40]을 Average(3)로 인코딩:
        // x=0: avg(0,10)=5 → raw=25; x=1: avg(30,20)=25 → raw=15
        var rawAvg = new byte[] { 0, 10, 20, 3, 25, 15 };
        Assert.True(PngDecoder.TryDecode(Png(2, 2, 0, rawAvg), 0, out _, out _, out var rgbaAvg));
        Assert.Equal(30, rgbaAvg[0]);
        Assert.Equal(40, rgbaAvg[4]);

        // 2x2 회색조: 0행 [10, 20] (None), 1행 [30, 40]을 Paeth(4)로 인코딩:
        // x=0: a=0,b=10,c=0 -> p=10 -> b=10 -> raw=20
        // x=1: a=30,b=20,c=10 -> p=40 -> pa=10,pb=20,pc=30 -> a=30 -> raw=10
        var rawPaeth = new byte[] { 0, 10, 20, 4, 20, 10 };
        Assert.True(PngDecoder.TryDecode(Png(2, 2, 0, rawPaeth), 0, out _, out _, out var rgbaPaeth));
        Assert.Equal(30, rgbaPaeth[0]);
        Assert.Equal(40, rgbaPaeth[4]);
    }

    [Fact]
    public void GrayAlpha_AndMultiIdat_DecodeCorrectly()
    {
        var raw = new byte[] { 0, 80, 128, 200, 255 };
        var ihdr = new byte[13];
        ihdr[3] = 2; ihdr[7] = 1; ihdr[8] = 8; ihdr[9] = 4; // Gray+Alpha
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) zs.Write(raw);
        byte[] idat = z.ToArray();
        int mid = idat.Length / 2;

        var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        png.Write(Chunk("IHDR", ihdr));
        png.Write(Chunk("IDAT", idat[..mid]));
        png.Write(Chunk("IDAT", idat[mid..]));
        png.Write(Chunk("IEND", Array.Empty<byte>()));

        Assert.True(PngDecoder.TryDecode(png.ToArray(), 0, out int w, out int h, out var rgba));
        Assert.Equal((2, 1), (w, h));
        Assert.Equal(new byte[] { 80, 80, 80, 128, 200, 200, 200, 255 }, rgba);
    }

    [Fact]
    public void UnsupportedTrnsOrBadFilter_ReturnsFalse()
    {
        Assert.False(PngDecoder.TryDecode(Png(1, 1, 2, new byte[] { 0, 1, 2, 3 }, trns: new byte[] { 0, 0, 0, 0, 0, 0 }), 0, out _, out _, out _));
        Assert.False(PngDecoder.TryDecode(Png(1, 1, 0, new byte[] { 5, 100 }), 0, out _, out _, out _));
    }
}
