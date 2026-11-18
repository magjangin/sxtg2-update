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
}
