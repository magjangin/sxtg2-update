using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace SixtarGateMod.Managers;

/// <summary>
/// sxtg2-compatible settings file: {game}\SaveCustomKey\config.txt (key=value, # or // comments).
/// Re-read whenever the file changes (checked when a play scene starts), so edits apply from the next song.
/// Keys the game already covers natively (NoteSway = 노트 흔들림 설정, Sudden = 실링 다운) and LaneCoverDebug are
/// kept in the file and ignored.
/// </summary>
public static class ModConfig
{
    private const string LOG_PREFIX = "[Config]";

    public static string ConfigPath { get; private set; } = "";
    private static DateTime _lastWrite = DateTime.MinValue;

    // [플레이]
    public static bool AutoPlay { get; private set; }
    public static bool AllPerfect { get; private set; }
    public static bool BlockSave { get; private set; } = true;
    public static float MaxScore { get; private set; } = 1000000f;
    public static bool IsMaxScoreCustom => Math.Abs(MaxScore - 1000000f) > 0.5f;
    public static bool ScoreInvertCountdown { get; private set; } = true;

    // [판정바]
    public static bool EnableJudgmentBar { get; private set; } = true;
    public static bool JudgmentBarVertical { get; private set; } = true;
    public static int JudgmentBarShape { get; private set; }
    public static int JudgmentBarRangeShape { get; private set; } = -1;
    public static string JudgmentBarSide { get; private set; } = "Center";

    // [키뷰어]
    public static bool EnableKeyViewer { get; private set; } = true;
    public static Color KeyViewerPressedColor { get; private set; } = new(0.15f, 0.75f, 0.85f, 0.85f);
    public static Color KeyViewerNormalColor { get; private set; } = new(0.08f, 0.08f, 0.08f, 0.65f);
    public static Color KeyViewerGatePressedColor { get; private set; } = new(1f, 0.25f, 0.5f, 0.9f);
    // [커스텀 노트 스킨]
    public static bool CustomNotes { get; private set; } = true;
    public static bool RandomNoteSkin { get; private set; }

    // [노트 속도 카오스]
    public static bool NoteSpeedChaos { get; private set; }
    public static float NoteSpeedChaosMin { get; private set; } = 0.6f;
    public static float NoteSpeedChaosMax { get; private set; } = 1.8f;
    public static bool NoteSpeedChaosPerLane { get; private set; } = true;

    // [히든 / 페이드] (percent of the lane: judgment line = 0, note spawn edge = 100)
    public static float Hidden { get; private set; }
    public static float LaneCoverSoftness { get; private set; } = 24f;
    public static bool FadeIn { get; private set; }
    public static float FadeInStart { get; private set; } = 100f;
    public static float FadeInLength { get; private set; } = 20f;
    public static bool FadeOut { get; private set; }
    public static float FadeOutStart { get; private set; } = 20f;
    public static float FadeOutLength { get; private set; } = 20f;
    public static bool AnyLaneCover => Hidden > 0f || FadeIn || FadeOut;

    // [화면]
    public static bool SkipAttendancePopup { get; private set; } = true;

    // [진단]
    public static bool DebugLog { get; private set; }

    // [커스텀 곡 마라톤]
    public static bool CustomMarathon { get; private set; }
    public static float CustomMarathonAutoNext { get; private set; }

    /// <summary>Scores written by AutoPlay / AllPerfect / a changed MaxScore must never reach the record files.</summary>
    public static bool ShouldBlockRecord => BlockSave || AutoPlay || AllPerfect || IsMaxScoreCustom;

    public static void Initialize(string gameFolder)
    {
        string dir = Path.Combine(gameFolder, "SaveCustomKey");
        ConfigPath = Path.Combine(dir, "config.txt");
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, DefaultTemplate);
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} Created default config: {ConfigPath}");
            }
            else
            {
                MigrateIfOutdated();
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} Could not create config: {ex.Message}");
        }
        ResetChallengeTogglesOnLaunch();
        ReloadIfChanged(force: true);
    }

    private const int TEMPLATE_VERSION = 6;

    /// <summary>
    /// An existing file (e.g. one copied from the sxtg2 Mono install) is rebuilt from the current template when its
    /// ConfigVersion is older, keeping every value the user set for keys the template still has. Keys for features
    /// the game already provides (NoteSway, Sudden) or that were dropped (LaneCoverDebug) disappear; the previous
    /// file is kept as config.txt.bak.
    /// </summary>
    private static void MigrateIfOutdated()
    {
        var old = Parse(File.ReadAllLines(ConfigPath));
        if (old.TryGetValue("ConfigVersion", out var v) && int.TryParse(v, out int ver) && ver >= TEMPLATE_VERSION)
            return;

        var lines = DefaultTemplate.Replace("\r", "").Split('\n');
        int kept = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("//")) continue;
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t.Substring(0, eq).Trim();
            if (key.Equals("ConfigVersion", StringComparison.OrdinalIgnoreCase)) continue;
            if (old.TryGetValue(key, out var value)) { lines[i] = $"{key}={value}"; kept++; }
        }

        File.Copy(ConfigPath, ConfigPath + ".bak", overwrite: true);
        File.WriteAllText(ConfigPath, string.Join("\n", lines));
        Melon<Core>.Logger.Msg($"{LOG_PREFIX} Updated config.txt to template v{TEMPLATE_VERSION} ({kept} values kept, old file saved as config.txt.bak)");
    }

    public static void ReloadIfChanged(bool force = false)
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var write = File.GetLastWriteTimeUtc(ConfigPath);
            if (!force && write == _lastWrite) return;
            _lastWrite = write;
            Apply(Parse(File.ReadAllLines(ConfigPath)));
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} Loaded: AutoPlay={AutoPlay}, AllPerfect={AllPerfect}, BlockSave={BlockSave}, MaxScore={MaxScore:0}, " +
                $"JudgmentBar={EnableJudgmentBar}(vertical={JudgmentBarVertical}, shape={JudgmentBarShape}, side={JudgmentBarSide}), KeyViewer={EnableKeyViewer}, CustomNotes={CustomNotes}, RandomNoteSkin={RandomNoteSkin}, " +
                $"SpeedChaos={NoteSpeedChaos}({NoteSpeedChaosMin}-{NoteSpeedChaosMax}, perLane={NoteSpeedChaosPerLane}), Hidden={Hidden}, FadeIn={FadeIn}, FadeOut={FadeOut}, " +
                $"Marathon={CustomMarathon}(autoNext={CustomMarathonAutoNext}s)");
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Error($"{LOG_PREFIX} Failed reading config: {ex.Message}");
        }
    }

    private static Dictionary<string, string> Parse(string[] lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return map;
    }

    private static void Apply(Dictionary<string, string> m)
    {
        AutoPlay = Bool(m, "AutoPlay", false);
        AllPerfect = Bool(m, "AllPerfect", false);
        BlockSave = Bool(m, "BlockSave", true);
        MaxScore = Float(m, "MaxScore", 1000000f);
        if (MaxScore <= 0f) MaxScore = 1000000f;
        ScoreInvertCountdown = Bool(m, "ScoreInvertCountdown", true);

        EnableJudgmentBar = Bool(m, "EnableJudgmentBar", true);
        JudgmentBarVertical = Bool(m, "JudgmentBarVertical", true);
        JudgmentBarShape = Math.Clamp(Int(m, "JudgmentBarShape", 0), 0, 2);
        JudgmentBarRangeShape = Math.Clamp(Int(m, "JudgmentBarRangeShape", -1), -1, 2);
        JudgmentBarSide = m.TryGetValue("JudgmentBarSide", out var side) && side.Length > 0 ? side : "Center";

        EnableKeyViewer = Bool(m, "EnableKeyViewer", true);
        KeyViewerPressedColor = ColorValue(m, "KeyViewerPressedColor", new Color(0.15f, 0.75f, 0.85f, 0.85f));
        KeyViewerNormalColor = ColorValue(m, "KeyViewerNormalColor", new Color(0.08f, 0.08f, 0.08f, 0.65f));
        KeyViewerGatePressedColor = ColorValue(m, "KeyViewerGatePressedColor", new Color(1f, 0.25f, 0.5f, 0.9f));
        CustomNotes = Bool(m, "CustomNotes", true);
        RandomNoteSkin = Bool(m, "RandomNoteSkin", false);

        NoteSpeedChaos = Bool(m, "NoteSpeedChaos", false);
        NoteSpeedChaosMin = Math.Clamp(Float(m, "NoteSpeedChaosMin", 0.6f), 0.1f, 10f);
        NoteSpeedChaosMax = Math.Clamp(Float(m, "NoteSpeedChaosMax", 1.8f), 0.1f, 10f);
        if (NoteSpeedChaosMax < NoteSpeedChaosMin) (NoteSpeedChaosMin, NoteSpeedChaosMax) = (NoteSpeedChaosMax, NoteSpeedChaosMin);
        NoteSpeedChaosPerLane = Bool(m, "NoteSpeedChaosPerLane", true);

        Hidden = Math.Clamp(Float(m, "Hidden", 0f), 0f, 90f);
        LaneCoverSoftness = Math.Max(0f, Float(m, "LaneCoverSoftness", 24f));
        FadeIn = Bool(m, "FadeIn", false);
        FadeInStart = Math.Clamp(Float(m, "FadeInStart", 100f), 0f, 100f);
        FadeInLength = Math.Clamp(Float(m, "FadeInLength", 20f), 0.1f, 100f);
        FadeOut = Bool(m, "FadeOut", false);
        FadeOutStart = Math.Clamp(Float(m, "FadeOutStart", 20f), 0f, 100f);
        FadeOutLength = Math.Clamp(Float(m, "FadeOutLength", 20f), 0.1f, 100f);

        CustomMarathon = Bool(m, "CustomMarathon", false);
        CustomMarathonAutoNext = Math.Max(0f, Float(m, "CustomMarathonAutoNext", 0f));

        SkipAttendancePopup = Bool(m, "SkipAttendancePopup", true);
        DebugLog = Bool(m, "DebugLog", false);
    }

    /// <summary>
    /// sxtg2 semantics: Sudden / Hidden / FadeIn / FadeOut return to 0 at every game launch (positions and lengths
    /// stay), so a challenge modifier left on in the file never surprises the next session.
    /// </summary>
    private static void ResetChallengeTogglesOnLaunch()
    {
        try
        {
            var lines = File.ReadAllLines(ConfigPath);
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                foreach (var key in new[] { "Sudden", "Hidden", "FadeIn", "FadeOut" })
                {
                    if (!t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) continue;
                    string v = t.Substring(key.Length + 1).Trim();
                    if (v != "0") { lines[i] = key + "=0"; changed = true; }
                }
            }
            if (changed)
            {
                File.WriteAllLines(ConfigPath, lines);
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} Reset Sudden/Hidden/FadeIn/FadeOut to 0 for this launch");
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} Could not reset challenge toggles: {ex.Message}");
        }
    }

    private static bool Bool(Dictionary<string, string> m, string key, bool def)
    {
        if (!m.TryGetValue(key, out var v) || v.Length == 0) return def;
        switch (v.Trim().ToLowerInvariant())
        {
            case "1": case "true": case "t": case "on": case "yes": case "y": case "enable": case "enabled":
            case "켜짐": case "켜기": case "사용": case "활성화": case "참": case "트루":
                return true;
            case "0": case "false": case "f": case "off": case "no": case "n": case "disable": case "disabled":
            case "꺼짐": case "끄기": case "미사용": case "비활성화": case "거짓": case "폴스":
                return false;
        }
        Melon<Core>.Logger.Warning($"{LOG_PREFIX} {key}: could not read \"{v}\", keeping default {def}");
        return def;
    }

    private static int Int(Dictionary<string, string> m, string key, int def)
        => m.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : def;

    private static float Float(Dictionary<string, string> m, string key, float def)
        => m.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : def;

    private static Color ColorValue(Dictionary<string, string> m, string key, Color def)
    {
        if (!m.TryGetValue(key, out var raw) || raw.Length == 0) return def;
        string v = raw.Trim().ToLowerInvariant();
        switch (v)
        {
            case "cyan": case "시안": case "청록": case "청록색": case "민트": case "민트색": return new Color(0.15f, 0.75f, 0.85f, 0.85f);
            case "magenta": case "pink": case "마젠타": case "분홍": case "분홍색": case "핫핑크": return new Color(1f, 0.25f, 0.5f, 0.9f);
            case "yellow": case "노랑": case "노란색": case "황색": return new Color(1f, 0.85f, 0.2f, 0.9f);
            case "red": case "빨강": case "빨간색": case "적색": return new Color(0.95f, 0.25f, 0.25f, 0.9f);
            case "blue": case "파랑": case "파란색": case "청색": return new Color(0.2f, 0.5f, 0.85f, 0.9f);
            case "green": case "초록": case "초록색": case "녹색": return new Color(0.25f, 0.85f, 0.35f, 0.9f);
            case "white": case "흰색": case "하양": case "백색": return new Color(0.95f, 0.95f, 0.95f, 0.9f);
            case "black": case "검정": case "검은색": case "흑색": return new Color(0.08f, 0.08f, 0.08f, 0.85f);
            case "orange": case "주황": case "주황색": return new Color(1f, 0.55f, 0.15f, 0.9f);
            case "purple": case "violet": case "보라": case "보라색": case "자색": return new Color(0.65f, 0.35f, 0.85f, 0.9f);
            case "sky": case "skyblue": case "하늘": case "하늘색": return new Color(0.4f, 0.75f, 1f, 0.9f);
        }

        string hex = v.TrimStart('#');
        if ((hex.Length == 6 || hex.Length == 8) && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba))
        {
            if (hex.Length == 6) rgba = (rgba << 8) | 0xFF;
            return new Color(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
        }

        var parts = v.Split(',');
        if (parts.Length == 3 || parts.Length == 4)
        {
            var f = new float[4] { 0, 0, 0, 1 };
            bool ok = true;
            for (int i = 0; i < parts.Length; i++)
                ok &= float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]);
            if (ok)
            {
                if (f[0] > 1f || f[1] > 1f || f[2] > 1f || f[3] > 1f)
                {
                    for (int i = 0; i < 3; i++) f[i] = Mathf.Clamp01(f[i] / 255f);
                    if (f[3] > 1f) f[3] = Mathf.Clamp01(f[3] / 255f);
                }
                return new Color(f[0], f[1], f[2], f[3]);
            }
        }

        Melon<Core>.Logger.Warning($"{LOG_PREFIX} {key}: could not read color \"{raw}\", keeping default");
        return def;
    }

    private const string DefaultTemplate =
@"# SixtarGateMod 설정 파일 (SaveCustomKey/config.txt) - sxtg2 형식과 같습니다.
# 저장하면 다음 곡(플레이 씬에 들어갈 때)부터 적용됩니다. 게임을 다시 켤 필요는 없습니다.
# 켜기/끄기 값: 1/0, true/false, 켜짐/꺼짐, on/off 모두 됩니다. # 또는 // 로 시작하는 줄은 주석입니다.
# 게임에 이미 있는 기능은 게임 설정을 쓰세요: 노트 흔들림 = ""노트 흔들림 설정"", 서든 = ""실링 다운"".

# 설정 파일 형식 버전 (모드가 관리합니다. 바꾸지 마세요)
ConfigVersion=6

# ===== [플레이] =====

# 오토 플레이 (1 = 켜짐, 0 = 꺼짐)
AutoPlay=0

# 올 퍼펙트 판정 조작 - BLUESTAR (1 = 켜짐, 0 = 꺼짐)
AllPerfect=0

# 베스트 스코어 / 기록 저장 차단 (1 = 켜짐, 0 = 꺼짐). AutoPlay/AllPerfect/MaxScore 변경 중에는 항상 차단됩니다.
BlockSave=1

# 점수 상한 (만점 기준값). 기본값 1000000 = 원본과 동일
MaxScore=1000000

# 게임 설정 ""점수 반전""을 켰을 때 숫자가 줄어드는 방식
# 1 = 판정마다 얻은 점수만큼 줄어서 곡 끝에 0이 됨 (만점 - 현재 점수)
# 0 = 게임 원래 방식: 놓친 점수만 빠짐 (전부 BLUESTAR면 만점 그대로)
ScoreInvertCountdown=1

# ===== [판정바] =====

# 실시간 판정바 표시 (1 = 켜짐, 0 = 꺼짐)
EnableJudgmentBar=1

# 판정바 방향 (1 = 세로, 0 = 가로)
JudgmentBarVertical=1

# 판정바 모양 (0 = 사각 바, 1 = 알약 캡슐, 2 = 삼각/다이아몬드)
JudgmentBarShape=0

# 판정바 안쪽 범위 박스 모양 (-1 = 판정바 모양 따라감, 0 = 사각, 1 = 알약, 2 = 삼각)
JudgmentBarRangeShape=-1

# 판정바 위치 (Left = 화면 왼쪽, Right = 화면 오른쪽, Center = 기본 위치)
# 기본 위치는 세로 판정바는 왼쪽, 가로 판정바는 화면 정중앙입니다.
JudgmentBarSide=Center

# ===== [키뷰어] =====

# 실시간 키뷰어 표시 (1 = 켜짐, 0 = 꺼짐)
EnableKeyViewer=1

# 키를 눌렀을 때 색 (일반 노트 키)
# 형식: #RRGGBB, #RRGGBBAA, R,G,B,A, 색 이름 (시안, 마젠타, 노랑, 빨강, 파랑, 초록, 흰색, 검정, 주황, 보라, 분홍, 하늘, 민트)
KeyViewerPressedColor=#26BFD9D9

# 누르지 않았을 때 색
KeyViewerNormalColor=#141414A6

# GATE 키(가운데 4번)를 눌렀을 때 색
KeyViewerGatePressedColor=#FF4081E6

# ===== [노트 속도 카오스] =====
# [챌린지] 노트마다 낙하 속도가 제각각이라 서로 추월합니다. 판정에는 영향이 없습니다.

# 속도 카오스 (1 = 켜짐, 0 = 꺼짐)
NoteSpeedChaos=0

# 속도 배율 최솟값 (1 = 원래 속도)
NoteSpeedChaosMin=0.6

# 속도 배율 최댓값
NoteSpeedChaosMax=1.8

# 1 = 레인마다 속도가 다름(같은 레인 안에서는 순서 유지), 0 = 노트마다 다름(완전 카오스)
NoteSpeedChaosPerLane=1

# ===== [커스텀 노트 스킨] =====

# 게임 폴더 CustomNotes 폴더의 PNG로 노트 모양을 바꿈 (1 = 켜짐, 0 = 꺼짐)
# Blue.png / Red.png / Gate.png = 노트 머리, Tail.png = 롱노트 끝, Hold.png = 롱노트 몸통 (BlueTail, RedHold 처럼 색별로도 가능)
CustomNotes=1

# 랜덤 노트 스킨: 노트가 나올 때마다 게임에 있는 노트 스킨(default, wacca, gothic, snowflake ...) 중 하나를 무작위로 입힘
# (1 = 켜짐, 0 = 꺼짐). 파랑/빨강/게이트 노트 종류는 그대로 유지됩니다. 켜면 위의 CustomNotes는 쓰이지 않습니다.
RandomNoteSkin=0

# ===== [히든] =====
# [챌린지] 판정선 쪽을 가립니다. 판정선 = 0%, 노트가 나오는 화면 끝 = 100%로 잽니다.
# Hidden은 게임을 켤 때마다 0(끔)으로 돌아갑니다. 켠 뒤 여기서 올리면 다음 곡부터 적용됩니다.

# 히든: 판정선 쪽을 가릴 비율 (%, 0 = 끔)
Hidden=0

# 가린 경계를 살짝 흐리는 폭 (픽셀, 0 = 칼같이 자름)
LaneCoverSoftness=24

# ===== [페이드 인 / 페이드 아웃] =====
# [챌린지] 노트가 서서히 나타나거나(페이드 인) 서서히 사라집니다(페이드 아웃).
# 위치·길이는 판정선 = 0%, 노트가 나오는 화면 끝 = 100% 기준입니다.
# FadeIn/FadeOut은 게임을 켤 때마다 0(끔)으로 돌아갑니다. 위치와 길이는 그대로 남습니다.

# 페이드 인: 내려오는 노트가 서서히 나타남 (1 = 켜짐, 0 = 꺼짐)
FadeIn=0

# 나타나기 시작하는 위치 (%). 100 = 화면 끝에서부터, 60 = 레인 60% 높이에서부터
FadeInStart=100

# 이 길이(%)만큼 내려오는 동안 투명 → 불투명. 예: 시작 100, 길이 20 → 80% 높이부터 완전히 보임
FadeInLength=20

# 페이드 아웃: 판정선에 다가가는 노트가 서서히 사라짐 (1 = 켜짐, 0 = 꺼짐)
FadeOut=0

# 사라지기 시작하는 위치 (%). 예: 40 = 레인 40% 높이부터 흐려지기 시작
FadeOutStart=20

# 이 길이(%)만큼 내려오는 동안 불투명 → 투명. 예: 시작 20, 길이 20 → 판정선에서 완전히 사라짐
FadeOutLength=20

# ===== [커스텀 곡 마라톤] =====

# 마라톤 (1 = 켜짐, 0 = 꺼짐). 커스텀 곡 하나를 시작하면 결과창 Enter로 아직 안 친 다음 커스텀 곡이 바로 시작됩니다.
# 모든 커스텀 곡을 한 번씩 돌면 목록으로 돌아갑니다. 고른 곡이 1번, 나머지는 매번 무작위(같은 곡은 다시 안 나옴).
# 결과창에서 Esc = 마라톤 그만하고 목록으로
CustomMarathon=0

# 결과창에서 몇 초 뒤 자동으로 다음 곡으로 넘어갈지 (0 = Enter를 누를 때만)
CustomMarathonAutoNext=0

# ===== [화면] =====

# 모드 선택 화면에 들어갈 때 출석 보상 창이 자동으로 뜨지 않게 함 (1 = 안 띄움, 0 = 게임 원래대로 띄움)
# 안 띄우면 그날 출석 보상(ARKK)도 받지 않은 상태로 남습니다. 받고 싶은 날은 0으로 바꾸고 게임을 다시 켜세요.
SkipAttendancePopup=1

# ===== [진단] =====

# 곡 선택/자켓/음원처럼 자주 일어나는 일도 MelonLoader 로그에 남김 (1 = 켜짐, 0 = 꺼짐). 문제를 찾을 때만 켜세요.
DebugLog=0
";
}
