using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SixtarGateMod.Loaders;

/// <summary>
/// Reads user text files (info.txt, BMS headers) that may not be UTF-8.
/// UTF-8 (BOM or strict decode) first; if the bytes are not valid UTF-8, the given legacy code pages are tried in order
/// with strict decoding, and the first that decodes without error wins. 0 means the system ANSI code page
/// (what Notepad's "ANSI" saved on this PC: CP949 on Korean Windows, GBK on Chinese Windows).
/// </summary>
public static class TextDecoder
{
    /// <summary>System ANSI code page, then GBK, Big5, Shift-JIS, CP949. For info.txt.</summary>
    public static readonly int[] InfoFallback = { 0, 936, 950, 932, 949 };
    /// <summary>Shift-JIS first (BMS convention), then the system ANSI code page. For BMS charts.</summary>
    public static readonly int[] BmsFallback = { 932, 0, 949, 936 };

    private static bool _providerRegistered;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ReadAllText(string path, int[] fallbackCodePages)
        => Decode(File.ReadAllBytes(path), fallbackCodePages, out _);

    public static string Decode(byte[] bytes, int[] fallbackCodePages, out string encodingName)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encodingName = "utf-8";
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encodingName = "utf-16le";
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encodingName = "utf-16be";
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            encodingName = "utf-8";
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException) { }

        EnsureProvider();
        foreach (int cp in fallbackCodePages)
        {
            int codePage = cp == 0 ? AnsiCodePage() : cp;
            if (codePage <= 0 || codePage == 65001) continue;
            try
            {
                var enc = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                string text = enc.GetString(bytes);
                encodingName = enc.WebName;
                return text;
            }
            catch (Exception) { }
        }

        encodingName = "utf-8 (lossy)";
        return Encoding.UTF8.GetString(bytes);
    }

    private static int AnsiCodePage()
    {
        // Only CJK double-byte pages: a single-byte page such as 1252 (invariant culture) would "decode" anything.
        try
        {
            int cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return cp is 932 or 936 or 949 or 950 ? cp : 0;
        }
        catch { return 0; }
    }

    private static void EnsureProvider()
    {
        if (_providerRegistered) return;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
        catch { }
        _providerRegistered = true;
    }
}
