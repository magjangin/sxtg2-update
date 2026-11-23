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

    /// <summary>
    /// 자켓 파일 크기·수정 시각("길이:틱"). 자켓 스프라이트 캐시 키에 들어가서, 같은 곡의 그림을 바꾸면(같은 파일명이어도)
    /// hwa 재스캔 뒤 새로 읽힌다(2026-11-24 전에는 곡 ID만 키여서 게임을 다시 켜야 했음).
    /// </summary>
    public string JacketStamp { get; set; } = "";
}
