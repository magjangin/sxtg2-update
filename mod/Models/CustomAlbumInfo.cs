using System;
using System.Collections.Generic;
using System.IO;

namespace SixtarGateMod.Models;

public class CustomAlbumInfo
{
    public string TrackId { get; set; } = "";
    public string AlbumFolder { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public float Bpm { get; set; } = 150f;
    public float Duration { get; set; } = 120f;
    public List<int> Difficulties { get; set; } = new() { 1, 4, 7, 10 };

    public string? BmsPath { get; set; }
    public string? BgmPath { get; set; }
    public string? PreviewPath { get; set; }
    public string? BgaPath { get; set; }
    public string? JacketPath { get; set; }

    public bool HasCustomBms => !string.IsNullOrEmpty(BmsPath) && File.Exists(BmsPath);
    public bool HasCustomBgm => !string.IsNullOrEmpty(BgmPath) && File.Exists(BgmPath);
    public bool HasCustomPreview => !string.IsNullOrEmpty(PreviewPath) && File.Exists(PreviewPath);
    public bool HasCustomBga => !string.IsNullOrEmpty(BgaPath) && File.Exists(BgaPath);
    public bool HasCustomJacket => !string.IsNullOrEmpty(JacketPath) && File.Exists(JacketPath);
}
