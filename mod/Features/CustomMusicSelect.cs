using HarmonyLib;
using Il2CppSixtarGate;
using Il2CppSixtarGate.MusicSelect;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// Music select fix for CUSTOM tracks: returning from the result screen lands on the played song.
/// </summary>
public static class CustomMusicSelect
{
    /// <summary>Track the player last confirmed in the CUSTOM tab (null after confirming an official track).</summary>
    public static string? LastCustomTrackId { get; set; }

    // SelectTrack stores the confirmed track in the "LastSelectedTrack" flag; LoadData later looks that ID up in
    // AllTracks (official tracks only), so a custom ID is never found and the list opens on its first song.
    [HarmonyPatch(typeof(TrackSelectController), "SelectTrack")]
    public static class TrackSelectController_SelectTrack_Patch
    {
        public static void Postfix(TrackSelectController __instance)
        {
            var track = __instance.SelectedTrack;
            if (track != null)
                LastCustomTrackId = CustomTrackManager.AlbumsByTrackId.ContainsKey(track.TrackID) ? track.TrackID : null;
        }
    }

    [HarmonyPatch(typeof(TrackSelectController), "LoadData")]
    public static class TrackSelectController_LoadData_Patch
    {
        public static void Postfix(TrackSelectController __instance)
        {
            string? id = LastCustomTrackId;
            if (id == null) return;
            var current = __instance.SelectedTrack;
            if (current != null && current.TrackID == id) return;

            var tracks = __instance.GetTracks(Core.CUSTOM_CATEGORY_NAME);
            if (tracks == null) return;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                if (t == null || t.TrackID != id) continue;
                __instance.SelectedTrack = t;
                Core.DebugLog($"[MusicSelect] Restored last custom track '{t.DisplayName}'");
                return;
            }
        }
    }
}
