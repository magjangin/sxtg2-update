using System;
using System.Collections.Generic;
using MelonLoader;

namespace SixtarGateMod.Managers;

/// <summary>
/// 작업 스레드에서도 쓸 수 있는 로그. <see cref="RunCaptured"/> 안에서 남긴 로그는 그 스레드에 모아 두었다가
/// 메인 스레드에서 <see cref="Flush"/>로 순서대로 출력하고, 그 밖에서는 바로 MelonLoader 로그로 보낸다.
/// 부팅 때 hwa 스캔을 Harmony 적용과 겹쳐 돌리면서(2026-11-15) 생김: 작업 스레드가 MelonLogger를 직접 부르면
/// 메인 스레드 로그 사이에 끼어 순서가 섞이고, 로거가 스레드 안전한지도 보장되지 않는다.
/// </summary>
internal static class ModLog
{
    internal enum Level { Debug, Msg, Warning, Error }

    internal readonly record struct Entry(Level Level, string Message);

    [ThreadStatic] private static List<Entry>? _capture;

    /// <summary>DebugLog=1일 때만 출력(출력 시점의 설정으로 판단).</summary>
    public static void Debug(string message) => Write(Level.Debug, message);
    public static void Msg(string message) => Write(Level.Msg, message);
    public static void Warning(string message) => Write(Level.Warning, message);
    public static void Error(string message) => Write(Level.Error, message);

    /// <summary>현재 스레드에서 <paramref name="work"/>를 돌리며 그동안의 로그를 모아 돌려준다.</summary>
    public static List<Entry> RunCaptured(Action work)
    {
        var previous = _capture;
        var buffer = new List<Entry>();
        _capture = buffer;
        try { work(); }
        finally { _capture = previous; }
        return buffer;
    }

    /// <summary>모아 둔 로그를 출력한다(메인 스레드에서).</summary>
    public static void Flush(List<Entry> entries)
    {
        foreach (var e in entries) Emit(e.Level, e.Message);
    }

    private static void Write(Level level, string message)
    {
        if (_capture != null) { _capture.Add(new Entry(level, message)); return; }
        Emit(level, message);
    }

    private static void Emit(Level level, string message)
    {
        var logger = Melon<Core>.Logger;
        switch (level)
        {
            case Level.Debug: Core.DebugLog(message); break;
            case Level.Msg: logger.Msg(message); break;
            case Level.Warning: logger.Warning(message); break;
            default: logger.Error(message); break;
        }
    }
}
