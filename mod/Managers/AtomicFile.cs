using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SixtarGateMod.Managers;

/// <summary>
/// 쓰기 도중 프로세스가 종료돼도 파일이 반쪽이 되지 않도록 임시 파일(.tmp)에 쓴 뒤 원자적으로 교체한다.
/// </summary>
internal static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static void WriteAllText(string path, string content)
    {
        EnsureParent(path);
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, content, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tmp);
            throw;
        }
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        EnsureParent(path);
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllLines(tmp, lines, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemp(tmp);
            throw;
        }
    }

    private static void TryDeleteTemp(string tmp)
    {
        try { if (File.Exists(tmp)) File.Delete(tmp); }
        catch (Exception) { /* 정리 실패는 무시하고 원래 예외를 전파 */ }
    }

    private static void EnsureParent(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}
