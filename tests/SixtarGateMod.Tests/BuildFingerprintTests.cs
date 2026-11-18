using System;
using System.Collections.Generic;
using SixtarGateMod.Managers;
using Xunit;

namespace SixtarGateMod.Tests;

public class BuildFingerprintTests
{
    [Fact]
    public void SameBuild_MatchesWhenAllEightKeysMatch()
    {
        var fp = new Dictionary<string, string>();
        foreach (var (key, _, _) in BuildFingerprint.Keys)
            fp[key] = "val_" + key;

        Assert.True(BuildFingerprint.SameBuild(fp, new Dictionary<string, string>(fp)));
    }

    [Fact]
    public void SameBuild_FalseWhenMissingOrUnknownTimestamp()
    {
        var a = new Dictionary<string, string>();
        foreach (var (key, _, _) in BuildFingerprint.Keys)
            a[key] = "val_" + key;

        var b = new Dictionary<string, string>(a) { ["gameAssemblyStamp"] = "?" };
        Assert.False(BuildFingerprint.SameBuild(a, b));
    }

    [Fact]
    public void VerifiedPlayTracker_RecordsOnCleanPlayToResult()
    {
        var tracker = new VerifiedPlayTracker();

        Assert.Equal(PlayCheck.None, tracker.OnScene("MusicSelect", false, 0, out _));
        Assert.Equal(PlayCheck.None, tracker.OnScene("Renewal_Play", true, 2, out _));
        Assert.Equal(PlayCheck.None, tracker.OnScene("Loading", true, 2, out _));
        var check = tracker.OnScene("Result", true, 2, out int during);

        Assert.Equal(PlayCheck.Passed, check);
        Assert.Equal(0, during);
    }

    [Fact]
    public void VerifiedPlayTracker_FlagsExceptionDuringPlay()
    {
        var tracker = new VerifiedPlayTracker();

        tracker.OnScene("Renewal_Play", true, 1, out _);
        var check = tracker.OnScene("Result", true, 3, out int during);

        Assert.Equal(PlayCheck.ExceptionsDuring, check);
        Assert.Equal(2, during);
    }

    [Fact]
    public void ParseAndFormat_RoundTripsAndSanitizesNewlines()
    {
        var fp = new Dictionary<string, string>();
        foreach (var (key, _, _) in BuildFingerprint.Keys)
            fp[key] = "v_" + key;

        var lines = BuildFingerprint.Format(fp, 118, new DateTime(2026, 11, 19, 8, 0, 0), "2026-11-19\nFakeSong");
        var parsed = BuildFingerprint.Parse(lines);

        Assert.NotNull(parsed);
        Assert.True(BuildFingerprint.SameBuild(fp, parsed!));
        Assert.Equal("2026-11-19 FakeSong", parsed![BuildFingerprint.VerifiedPlayKey]);
    }

    [Fact]
    public void Diff_FlagsMetadataChangeAsRiskyAndIgnoresUnknownValues()
    {
        var prev = new Dictionary<string, string>
        {
            ["buildId"] = "100",
            ["gameAssemblySize"] = "5000",
            ["metadataSize"] = "1000",
            ["unityPlayerSize"] = "?",
        };
        var cur = new Dictionary<string, string>
        {
            ["buildId"] = "100",
            ["gameAssemblySize"] = "5000",
            ["metadataSize"] = "2000",
            ["unityPlayerSize"] = "9999",
        };

        var changes = BuildFingerprint.Diff(prev, cur, out bool risky);

        Assert.Single(changes);
        Assert.True(risky);
    }

    [Fact]
    public void PeTimestamp_ParsesValidSyntheticHeaderAndRejectsBadMagic()
    {
        var buf = new byte[0x100];
        buf[0] = (byte)'M';
        buf[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(buf, 0x3C);
        buf[0x80] = (byte)'P';
        buf[0x81] = (byte)'E';
        BitConverter.GetBytes(0x69123456u).CopyTo(buf, 0x88);

        Assert.Equal("69123456", BuildFingerprint.PeTimestamp(buf));
        Assert.Equal("?", BuildFingerprint.PeTimestamp(new byte[16]));
    }

    [Fact]
    public void MetadataHeader_ParsesVersionAndDetectsSectionTableMutation()
    {
        var buf = new byte[0x120];
        BitConverter.GetBytes(0xFAB11BAFu).CopyTo(buf, 0);
        BitConverter.GetBytes(31).CopyTo(buf, 4);
        buf[0x20] = 0x42;

        BuildFingerprint.MetadataHeader(buf, out string ver1, out string hash1);
        buf[0x20] = 0x43;
        BuildFingerprint.MetadataHeader(buf, out string ver2, out string hash2);

        Assert.Equal("31", ver1);
        Assert.Equal("31", ver2);
        Assert.NotEqual("?", hash1);
        Assert.NotEqual(hash1, hash2);
    }
}
