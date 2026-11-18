using System.Text;
using SixtarGateMod.Loaders;
using Xunit;

namespace SixtarGateMod.Tests;

public class TextDecoderTests
{
    static TextDecoderTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void Decode_Utf8Bom_StripsBomAndDecodes()
    {
        byte[] payload = Encoding.UTF8.GetBytes("곡명: 테스트");
        byte[] withBom = new byte[3 + payload.Length];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        payload.CopyTo(withBom, 3);

        string text = TextDecoder.Decode(withBom, TextDecoder.InfoFallback, out string enc);

        Assert.Equal("utf-8", enc);
        Assert.Equal("곡명: 테스트", text);
    }

    [Fact]
    public void Decode_ShiftJis_FallsBackToCp932()
    {
        byte[] sjis = Encoding.GetEncoding(932).GetBytes("#TITLE どりーむもーど");

        string text = TextDecoder.Decode(sjis, TextDecoder.BmsFallback, out string enc);

        Assert.Equal("#TITLE どりーむもーど", text);
        Assert.NotEqual("utf-8", enc);
    }

    [Fact]
    public void Decode_Cp949_FallsBackWhenNotUtf8()
    {
        byte[] cp949 = Encoding.GetEncoding(949).GetBytes("Title: 하얀 그리움");

        string text = TextDecoder.Decode(cp949, new[] { 949 }, out _);

        Assert.Equal("Title: 하얀 그리움", text);
    }
}
