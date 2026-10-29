using System.Collections.Generic;

namespace SixtarGateMod.Models;

public class CustomAlbumInfo
{
    public string TrackId { get; set; } = "";
    public string AlbumFolder { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public float Bpm { get; set; } = 150f;
    public float Duration { get; set; } = 120f;
    public List<int> Difficulties { get; set; } = new() { 1, 4, 7, 10 };

    // Set at scan time only for files that exist. The Has* checks run inside per-frame / per-row hooks
    // (BackgroundUIProcessor.IsVideoPlayable, jacket callbacks), so they must not touch the disk.
    public string? BmsPath { get; set; }
    public string? BgmPath { get; set; }
    public string? PreviewPath { get; set; }
    public string? BgaPath { get; set; }
    public string? JacketPath { get; set; }

    public bool HasCustomBms => BmsPath != null;
    public bool HasCustomBgm => BgmPath != null;
    public bool HasCustomPreview => PreviewPath != null;
    public bool HasCustomBga => BgaPath != null;
    public bool HasCustomJacket => JacketPath != null;
}
