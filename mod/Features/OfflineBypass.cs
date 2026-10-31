#nullable disable
using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using Il2CppInventory;
using Il2CppRhythmGame;
using Il2CppRhythmGame.LicenseTest;
using Il2CppRhythmGame.ModeSelect;
using Il2CppRhythmGame.SixtarServer;
using Il2CppSixtarGate;
using Il2CppSixtarGate.ModeSelect;
using Il2CppSixtarGate.Play;
using Il2CppSixtarGate.Session;
using Il2CppSixtarGate.Web;
using SixtarGateMod.Managers;

namespace SixtarGateMod.Features;

/// <summary>
/// Full offline / server-shutdown bypass for Sixtar Gate: STARTRAIL (Build 26218612).
///
/// Covers both:
/// 1. The active Renewal + BACKND pipeline (SixtarGate.Web.BackEnd*, TableLoader, TrackLoader,
///    ItemProvideModule, ClientDataProvider, UpdateCheckController, ArkkRewardModule, UnitySession).
/// 2. The legacy HTTP & data pipeline ported from sxtg2 saver test (ManagerWebRequest, UpdateChecker,
///    UserAccountModule, ItemHelper, ManagerPopup).
///
/// Every target method below has been verified in il2cpp_dump/script.json to have a unique
/// native address (ICF=1). Do not patch NeedClientUpdatePopup.OpenPopup or
/// ServerDisconnectedPopup.OpenPopup (both fold into 0x450BA0, ICF=5192).
/// </summary>
public static class OfflineBypass
{
    private const int DefaultOfflineArkk = 999999;
    private const int DefaultLicenseLevel = 10;

    private static Users EnsureOfflineUsers(BackEndWebRequest instance)
    {
        var user = instance._userData;
        if (user == null)
        {
            user = new Users
            {
                Nickname = "OfflinePlayer",
                Displayname = "OfflinePlayer",
                Iconid = "default",
                Bannerid = "default",
                LicenseSolar = DefaultLicenseLevel,
                LicenseLunar = DefaultLicenseLevel,
            };
            instance._userData = user;
        }
        return user;
    }

    // =========================================================================
    // 1. Renewal BACKND Web Module Boot & Maintenance Guards (SixtarGate.Web)
    // =========================================================================

    /// <summary>
    /// BackEndWebRequest.InitializeModule (RVA 0x580210, ICF=1):
    /// Original starts InitializeCoroutine, which calls Backend.InitializeAsyncByMultiProject,
    /// Request.Login, BackEndDataBase.InitializeDataBase, RequestStartupMaintenance, and
    /// BackEndChart.LoadChartInfoAsync. When offline or BACKND is shut down, InitializeCoroutine
    /// calls Exit() and never sets HasInitialized = true, hanging TableLoader, TrackLoader, and
    /// ItemProvideModule forever in WaitUntil(() => Web.HasInitialized).
    /// </summary>
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.InitializeModule))]
    public static class BackEndWebRequest_InitializeModule_Patch
    {
        public static bool Prefix(BackEndWebRequest __instance)
        {
            if (!Core.BlockServerUpload) return true;

            try
            {
                // Touch lazy sub-service getters so _requestService, _userDataService,
                // _shopService, and _rankingService are instantiated before modules query them.
                _ = __instance.Request;
                _ = __instance.UserData;
                _ = __instance.Shop;
                _ = __instance.Ranking;

                EnsureOfflineUsers(__instance);
                __instance._init = true;
                __instance.HasInitialized = true;
                __instance.LicenseRecoveryState = LicenseRecoveryState.Ready;

                Melon<Core>.Logger.Msg("[OfflineBypass] BackEndWebRequest initialized in offline mode (skipped BACKND login & maintenance check).");
            }
            catch (Exception ex)
            {
                Melon<Core>.Logger.Warning($"[OfflineBypass] BackEndWebRequest offline init warning: {ex.Message}");
                __instance.HasInitialized = true;
            }
            return false;
        }
    }

    // RVA 0x581400, ICF=1
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.ShowMaintenance))]
    public static class BackEndWebRequest_ShowMaintenance_Patch
    {
        public static bool Prefix() => !Core.SuppressMaintenancePopup;
    }

    // RVA 0x57FF10, ICF=1
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.CheckMaintenanceStatus))]
    public static class BackEndWebRequest_CheckMaintenanceStatus_Patch
    {
        public static bool Prefix() => !Core.SuppressMaintenancePopup;
    }

    // RVA 0x580050, ICF=1 — prevents BACKND failure from quitting the client
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.Exit))]
    public static class BackEndWebRequest_Exit_Patch
    {
        public static bool Prefix()
        {
            if (!Core.BlockServerUpload) return true;
            Melon<Core>.Logger.Msg("[OfflineBypass] Blocked BackEndWebRequest.Exit() quit request.");
            return false;
        }
    }

    // RVA 0x55EFA0, ICF=1
    [HarmonyPatch(typeof(BackEndMaintenance), nameof(BackEndMaintenance.Enter))]
    public static class BackEndMaintenance_Enter_Patch
    {
        public static bool Prefix() => !Core.SuppressMaintenancePopup;
    }

    // RVA 0x55F1D0, ICF=1
    [HarmonyPatch(typeof(BackEndMaintenance), nameof(BackEndMaintenance.Observe))]
    public static class BackEndMaintenance_Observe_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!Core.SuppressMaintenancePopup) return true;
            __result = false;
            return false;
        }
    }

    // RVA 0x5808A0, ICF=1
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.LoadUserData), new Type[] { typeof(Il2CppSystem.Action<Users>) })]
    public static class BackEndWebRequest_LoadUserData_Patch
    {
        public static bool Prefix(BackEndWebRequest __instance, Il2CppSystem.Action<Users> callback)
        {
            if (!Core.BlockServerUpload) return true;
            var user = EnsureOfflineUsers(__instance);
            callback?.Invoke(user);
            return false;
        }
    }

    // RVA 0x581A50, ICF=1
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.UpdateUserData), new Type[] { typeof(Users), typeof(Il2CppSystem.Action) })]
    public static class BackEndWebRequest_UpdateUserData_Public_Patch
    {
        public static bool Prefix(BackEndWebRequest __instance, Users user, Il2CppSystem.Action completeCallback)
        {
            if (!Core.BlockServerUpload) return true;
            if (user != null) __instance._userData = user;
            completeCallback?.Invoke();
            return false;
        }
    }

    // RVA 0x581910, ICF=1
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.UpdateUserData), new Type[] { typeof(Il2CppSystem.Action<bool>) })]
    public static class BackEndWebRequest_UpdateUserData_Private_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<bool> complete)
        {
            if (!Core.BlockServerUpload) return true;
            complete?.Invoke(true);
            return false;
        }
    }

    // RVA 0x5811E0, ICF=1
    [HarmonyPatch(typeof(BackEndWebRequest), nameof(BackEndWebRequest.SaveUserProfile), new Type[] { typeof(Users), typeof(Il2CppSystem.Action<bool>) })]
    public static class BackEndWebRequest_SaveUserProfile_Patch
    {
        public static bool Prefix(BackEndWebRequest __instance, Users profile, Il2CppSystem.Action<bool> complete)
        {
            if (!Core.BlockServerUpload) return true;
            if (profile != null) __instance._userData = profile;
            complete?.Invoke(true);
            return false;
        }
    }

    // =========================================================================
    // 2. Renewal BackEndRequestService (Tables, Server Version, Attendance, Login)
    // =========================================================================

    /// <summary>
    /// BackEndRequestService.GetTableVersions (RVA 0x57D1D0, ICF=1):
    /// TableLoader.LoadAsyncData waits for this callback. Returning an empty list causes
    /// TableLoader to skip remote chart downloads, mark TableLoader.HasInitialized = true,
    /// and use either cached tables on disk or the built-in serialized SXGT_TextAsset tables.
    /// </summary>
    [HarmonyPatch(typeof(BackEndRequestService), nameof(BackEndRequestService.GetTableVersions))]
    public static class BackEndRequestService_GetTableVersions_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<IRequestService.ITableVersion>> getTableVersions)
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog("[OfflineBypass] BackEndRequestService.GetTableVersions -> returning empty list (using local/built-in tables).");
            getTableVersions?.Invoke(new Il2CppSystem.Collections.Generic.List<IRequestService.ITableVersion>());
            return false;
        }
    }

    // RVA 0x57CEC0, ICF=1
    [HarmonyPatch(typeof(BackEndRequestService), nameof(BackEndRequestService.FetchServer))]
    public static class BackEndRequestService_FetchServer_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<IRequestService.ISixtarServerURL> getServerURL)
        {
            if (!Core.BlockServerUpload) return true;
            getServerURL?.Invoke(null);
            return false;
        }
    }

    // RVA 0x57CF80, ICF=1
    [HarmonyPatch(typeof(BackEndRequestService), nameof(BackEndRequestService.GetAttendanceEventTable))]
    public static class BackEndRequestService_GetAttendanceEventTable_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<IRequestService.IAttendanceTable> getAttendanceTable)
        {
            if (!Core.BlockServerUpload) return true;
            getAttendanceTable?.Invoke(null);
            return false;
        }
    }

    // RVA 0x57D6C0, ICF=1
    [HarmonyPatch(typeof(BackEndRequestService), nameof(BackEndRequestService.Login))]
    public static class BackEndRequestService_Login_Patch
    {
        public static bool Prefix(string accountID, Il2CppSystem.Action<IRequestService.ISixtarUserData, long> callback)
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog($"[OfflineBypass] BackEndRequestService.Login('{accountID}') -> offline success.");
            callback?.Invoke(null, 0L);
            return false;
        }
    }

    // =========================================================================
    // 3. Renewal BackEndUserDataService (Unlocks, Arkk, Rewards, License Test)
    // =========================================================================

    /// <summary>
    /// BackEndUserDataService.GetUnlockData (RVA 0x57F450, ICF=1):
    /// Both TrackLoader.LoadUnlockItems and ItemProvideModule.LoadItemData wait for this
    /// callback before setting HasInitialized = true.
    /// </summary>
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.GetUnlockData))]
    public static class BackEndUserDataService_GetUnlockData_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<string>> getUnlockItemList)
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog("[OfflineBypass] BackEndUserDataService.GetUnlockData -> returning empty list (all items unlocked locally).");
            getUnlockItemList?.Invoke(new Il2CppSystem.Collections.Generic.List<string>());
            return false;
        }
    }

    // RVA 0x57F550, ICF=1
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.GetUserArkk))]
    public static class BackEndUserDataService_GetUserArkk_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<int> getArkk)
        {
            if (!Core.BlockServerUpload) return true;
            getArkk?.Invoke(DefaultOfflineArkk);
            return false;
        }
    }

    // RVA 0x57F8D0, ICF=1
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.RefreshUserData))]
    public static class BackEndUserDataService_RefreshUserData_Patch
    {
        public static bool Prefix() => !Core.BlockServerUpload;
    }

    // RVA 0x57F950, ICF=1
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.RequestItemUnlock))]
    public static class BackEndUserDataService_RequestItemUnlock_Patch
    {
        public static bool Prefix(string itemID, Il2CppSystem.Action<bool> result)
        {
            if (!Core.BlockServerUpload) return true;
            result?.Invoke(true);
            return false;
        }
    }

    // RVA 0x57F3D0, ICF=1
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.GetAttendanceReward))]
    public static class BackEndUserDataService_GetAttendanceReward_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<IUserDataService.IAttendanceReward> getReward)
        {
            if (!Core.BlockServerUpload) return true;
            getReward?.Invoke(null);
            return false;
        }
    }

    /// <summary>
    /// BackEndUserDataService.RequestTravelModeArkkRewards (RVA 0x57FAD0, ICF=1):
    /// ArkkRewardModule on the Result screen waits for this callback before calling FinishInitialize().
    /// Computes local rewards via TravelArkkRewardProvider without touching BACKND DB.
    /// </summary>
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.RequestTravelModeArkkRewards))]
    public static class BackEndUserDataService_RequestTravelModeArkkRewards_Patch
    {
        public static bool Prefix(
            IPatternPlayer.IResultArgs resultArgs,
            Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<IUserDataService.IArkkReward>> rewards)
        {
            if (!Core.BlockServerUpload) return true;
            Il2CppSystem.Collections.Generic.List<IUserDataService.IArkkReward> list = null;
            try
            {
                if (resultArgs != null)
                {
                    var provider = new TravelArkkRewardProvider(resultArgs);
                    var enumerable = provider.GetRewards();
                    if (enumerable != null)
                        list = new Il2CppSystem.Collections.Generic.List<IUserDataService.IArkkReward>(enumerable);
                }
            }
            catch
            {
                // Fall back to empty reward list if local reward provider fails on custom tracks.
            }
            list ??= new Il2CppSystem.Collections.Generic.List<IUserDataService.IArkkReward>();
            rewards?.Invoke(list);
            return false;
        }
    }

    // RVA 0x57FAB0, ICF=1
    [HarmonyPatch(typeof(BackEndUserDataService), nameof(BackEndUserDataService.RequestStartLicenseTest))]
    public static class BackEndUserDataService_RequestStartLicenseTest_Patch
    {
        public static bool Prefix(int cost, Il2CppSystem.Action success)
        {
            if (!Core.BlockServerUpload) return true;
            success?.Invoke();
            return false;
        }
    }

    // =========================================================================
    // 4. Renewal BackEndShopService & BackEndRankingService
    // =========================================================================

    // RVA 0x57EDB0, ICF=1
    [HarmonyPatch(typeof(BackEndShopService), nameof(BackEndShopService.RequestItemPurchase))]
    public static class BackEndShopService_RequestItemPurchase_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<bool> success)
        {
            if (!Core.BlockServerUpload) return true;
            success?.Invoke(true);
            return false;
        }
    }

    // RVA 0x561390, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService), nameof(BackEndRankingService.GetHighScoreList))]
    public static class BackEndRankingService_GetHighScoreList_Patch
    {
        public static bool Prefix(
            Il2CppSystem.Action<
                Il2CppSystem.Collections.Generic.List<IRankingService.IRankingData>,
                Il2CppSystem.Collections.Generic.List<IRankingService.IRankingData>> callback)
        {
            if (!Core.BlockServerUpload) return true;
            var empty1 = new Il2CppSystem.Collections.Generic.List<IRankingService.IRankingData>();
            var empty2 = new Il2CppSystem.Collections.Generic.List<IRankingService.IRankingData>();
            callback?.Invoke(empty1, empty2);
            return false;
        }
    }

    // RVA 0x561590, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService), nameof(BackEndRankingService.GetRankingList))]
    public static class BackEndRankingService_GetRankingList_Patch
    {
        public static bool Prefix(
            TravelRankingKey key,
            TravelRankingListKind kind,
            Il2CppSystem.Action<TravelRankingQueryResult> callback)
        {
            if (!Core.BlockServerUpload) return true;
            var empty = new Il2CppSystem.Collections.Generic.List<IRankingService.IRankingData>();
            var queryResult = new TravelRankingQueryResult(
                key,
                kind,
                TravelRankingQueryStatus.Success,
                empty,
                0,
                true,
                true,
                "offline");
            callback?.Invoke(queryResult);
            return false;
        }
    }

    // RVA 0x577840, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService.TravelRankingReadRequest), nameof(BackEndRankingService.TravelRankingReadRequest.Start))]
    public static class BackEndRankingService_TravelRankingReadRequest_Start_Patch
    {
        public static bool Prefix() => !Core.BlockServerUpload;
    }

    // RVA 0x5623E0, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService), nameof(BackEndRankingService.PostHighScoreData))]
    public static class BackEndRankingService_PostHighScoreData_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<int> getBestScore)
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog("[OfflineBypass] Blocked BackEndRankingService.PostHighScoreData -> emulated rank 1.");
            getBestScore?.Invoke(1);
            return false;
        }
    }

    // RVA 0x562730, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService), nameof(BackEndRankingService.SubmitTravelHighScore))]
    public static class BackEndRankingService_SubmitTravelHighScore_Patch
    {
        public static bool Prefix(TravelRankingCandidate candidate, Il2CppSystem.Action<TravelRankingSubmitResult> callback)
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog("[OfflineBypass] Blocked BackEndRankingService.SubmitTravelHighScore -> emulated local success.");
            int score = candidate != null ? candidate.Score : 0;
            callback?.Invoke(new TravelRankingSubmitResult(TravelRankingSubmitStatus.Confirmed, score, "offline"));
            return false;
        }
    }

    // RVA 0x585C50, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService.TravelHighScoreRequest), nameof(BackEndRankingService.TravelHighScoreRequest.Start))]
    public static class BackEndRankingService_TravelHighScoreRequest_Start_Patch
    {
        public static bool Prefix()
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog("[OfflineBypass] Prevented BackEnd TravelHighScoreRequest from uploading to BACKND server.");
            return false;
        }
    }

    // RVA 0x561660, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService), nameof(BackEndRankingService.GetUserLicenseLevel))]
    public static class BackEndRankingService_GetUserLicenseLevel_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<IRankingService.ILicenseLevel> callback)
        {
            if (!Core.BlockServerUpload) return true;
            var level = new BackEndRankingService.LicenseLevel();
            callback?.Invoke(level.Cast<IRankingService.ILicenseLevel>());
            return false;
        }
    }

    // RVA 0x561C00, ICF=1
    [HarmonyPatch(typeof(BackEndRankingService), nameof(BackEndRankingService.PostClearLicense))]
    public static class BackEndRankingService_PostClearLicense_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<long> getUpdatedTimeStamp)
        {
            if (!Core.BlockServerUpload) return true;
            getUpdatedTimeStamp?.Invoke(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return false;
        }
    }

    // =========================================================================
    // 5. Renewal ModeSelect Version Check, Popups, UnitySession & Steam Guard
    // =========================================================================

    /// <summary>
    /// UpdateCheckController.Initialize (RVA 0x53F1A0, ICF=1):
    /// On ModeSelect entry, original sets VersionCheckPass = false and opens UpdateCheckPopup,
    /// which calls Web.Request.FetchServer and blocks ModeSelect with NeedClientUpdatePopup
    /// if the server is unreachable.
    /// </summary>
    [HarmonyPatch(typeof(UpdateCheckController), nameof(UpdateCheckController.Initialize))]
    public static class UpdateCheckController_Initialize_Patch
    {
        public static bool Prefix(UpdateCheckController __instance)
        {
            if (!Core.BlockServerUpload) return true;
            UpdateCheckController._firstCheck = true;
            UpdateCheckController._passed = true;
            if (__instance != null) __instance.VersionCheckPass = true;
            Core.DebugLog("[OfflineBypass] Skipped ModeSelect UpdateCheckController version check (VersionCheckPass=true).");
            return false;
        }
    }

    // RVA 0x49A830, ICF=1
    [HarmonyPatch(typeof(UpdateCheckPopup), nameof(UpdateCheckPopup.OpenPopup))]
    public static class UpdateCheckPopup_OpenPopup_Patch
    {
        public static bool Prefix(UpdateCheckPopup __instance)
        {
            if (!Core.BlockServerUpload) return true;
            UpdateCheckController._firstCheck = true;
            UpdateCheckController._passed = true;
            __instance.OnNeedUpdate?.Invoke(false);
            __instance.Close();
            return false;
        }
    }

    // RVA 0x491C10, ICF=1
    [HarmonyPatch(typeof(ServerMaintenanceNotice), nameof(ServerMaintenanceNotice.Show), new Type[] { typeof(string) })]
    public static class ServerMaintenanceNotice_Show_Patch
    {
        public static bool Prefix(string language)
        {
            if (!Core.SuppressMaintenancePopup) return true;
            Melon<Core>.Logger.Msg($"[OfflineBypass] Suppressed ServerMaintenanceNotice popup (Language: {language})");
            return false;
        }
    }

    // RVA 0x579BA0, ICF=1
    [HarmonyPatch(typeof(UnitySessionLegacyConnector), nameof(UnitySessionLegacyConnector.Connect))]
    public static class UnitySessionLegacyConnector_Connect_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<bool> resultCallback)
        {
            if (!Core.BlockServerUpload) return true;
            resultCallback?.Invoke(false);
            return false;
        }
    }

    // RVA 0x57A510, ICF=1 (typo-tolerant: ReqeustSessionPlayingCount / RequestSessionPlayingCount)
    [HarmonyPatch]
    public static class UnitySessionLegacyConnector_ReqeustSessionPlayingCount_Patch
    {
        public static MethodBase TargetMethod()
            => PatchResilience.ResolveMethod(typeof(UnitySessionLegacyConnector), "ReqeustSessionPlayingCount", "RequestSessionPlayingCount");

        public static bool Prefix(Il2CppSystem.Action<int> callback)
        {
            if (!Core.BlockServerUpload) return true;
            callback?.Invoke(0);
            return false;
        }
    }

    /// <summary>
    /// ClientDataProvider.InitializeModule (RVA 0x79B930, ICF=1):
    /// If Steam is not running, SteamClient.Init(1802720, true) throws an exception before
    /// SixtarUser is registered and before HasInitialized is set to true. The finalizer
    /// catches that failure and falls back to InitializeUnityEditor() (RVA 0x79BDB0), which
    /// creates a local SixtarUser and sets HasInitialized = true.
    /// </summary>
    [HarmonyPatch(typeof(ClientDataProvider), nameof(ClientDataProvider.InitializeModule))]
    public static class ClientDataProvider_InitializeModule_Patch
    {
        public static Exception Finalizer(ClientDataProvider __instance, Exception __exception)
        {
            if (__exception == null) return null;
            try
            {
                Melon<Core>.Logger.Warning($"[OfflineBypass] SteamClient init failed ({__exception.Message}); falling back to offline editor user profile.");
                __instance.InitializeUnityEditor();
            }
            catch (Exception fallbackEx)
            {
                Melon<Core>.Logger.Error($"[OfflineBypass] ClientDataProvider fallback error: {fallbackEx.Message}");
                __instance.HasInitialized = true;
            }
            return null;
        }
    }

    // RVA 0x7995A0, ICF=1 — swallow Steamworks exceptions when Steam is closed
    [HarmonyPatch(typeof(AchievementModule), nameof(AchievementModule.InitializeModule))]
    public static class AchievementModule_InitializeModule_Patch
    {
        public static Exception Finalizer(AchievementModule __instance, Exception __exception)
        {
            if (__exception == null) return null;
            __instance.HasInitialized = true;
            return null;
        }
    }

    // RVA 0x798930, ICF=1
    [HarmonyPatch(typeof(AchievementModule), nameof(AchievementModule.ContainsAchievement))]
    public static class AchievementModule_ContainsAchievement_Patch
    {
        public static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;
            __result = false;
            return null;
        }
    }

    // RVA 0x799870, ICF=1 (typo-tolerant: UnlockReqeustAchievement / UnlockRequestAchievement)
    [HarmonyPatch]
    public static class AchievementModule_UnlockReqeustAchievement_Patch
    {
        public static MethodBase TargetMethod()
            => PatchResilience.ResolveMethod(typeof(AchievementModule), "UnlockReqeustAchievement", "UnlockRequestAchievement");

        public static Exception Finalizer(Exception __exception) => null;
    }

    // =========================================================================
    // 6. Legacy Pipeline Ported from sxtg2 saver test
    //    (ManagerWebRequest, UpdateChecker, UserAccountModule, ItemHelper)
    // =========================================================================

    // RVA 0x59D560, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.CommonErrorAction))]
    public static class ManagerWebRequest_CommonErrorAction_Patch
    {
        public static bool Prefix(ref Il2CppSystem.Collections.IEnumerator __result)
        {
            if (!Core.BlockServerUpload) return true;
            try
            {
                var popup = ManagerPopup.Instance;
                if (popup != null)
                {
                    popup.HidePopupOverlay();
                    popup.CloseAllPopup();
                }
            }
            catch { }
            __result = new Il2CppSystem.Collections.ArrayList().GetEnumerator();
            return false;
        }
    }

    // RVA 0x59D5F0, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.FetchServer), new Type[] { typeof(Il2CppSystem.Action<bool>) })]
    public static class ManagerWebRequest_FetchServer_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<bool> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(true);
            return false;
        }
    }

    // RVA 0x59E570, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetUnlockData))]
    public static class ManagerWebRequest_GetUnlockData_Patch
    {
        public static bool Prefix()
        {
            if (!Core.BlockServerUpload) return true;
            try
            {
                var helper = ItemHelper.Instance;
                if (helper != null)
                    helper.InitializeUnlockData(new Il2CppSystem.Collections.Generic.List<string>());
            }
            catch { }
            return false;
        }
    }

    // RVA 0x59EC40, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.RefreshRequest))]
    public static class ManagerWebRequest_RefreshRequest_Patch
    {
        public static bool Prefix() => !Core.BlockServerUpload;
    }

    // RVA 0x59EF80, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.RequestItemUnlock), new Type[] { typeof(string), typeof(Il2CppSystem.Action<bool>) })]
    public static class ManagerWebRequest_RequestItemUnlock_Patch
    {
        public static bool Prefix(string _itemID, Il2CppSystem.Action<bool> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(true);
            return false;
        }
    }

    // RVA 0x59E690, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetUserArkk), new Type[] { typeof(Il2CppSystem.Action<int>) })]
    public static class ManagerWebRequest_GetUserArkk_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<int> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(DefaultOfflineArkk);
            return false;
        }
    }

    // RVA 0x59D430, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.CheckServerConnection), new Type[] { typeof(string), typeof(Il2CppSystem.Action<bool>) })]
    public static class ManagerWebRequest_CheckServerConnection_Patch
    {
        public static bool Prefix(string httpURL, Il2CppSystem.Action<bool> onFinish)
        {
            if (!Core.BlockServerUpload) return true;
            onFinish?.Invoke(true);
            return false;
        }
    }

    // RVA 0x59E060, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetRemoteVersion), new Type[] { typeof(string), typeof(Il2CppSystem.Action<int>) })]
    public static class ManagerWebRequest_GetRemoteVersion_Patch
    {
        public static bool Prefix(string abName, Il2CppSystem.Action<int> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(207);
            return false;
        }
    }

    // RVA 0x59D770, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetAttendanceEventTable), new Type[] { typeof(Il2CppSystem.Action) })]
    public static class ManagerWebRequest_GetAttendanceEventTable_Patch
    {
        public static bool Prefix(Il2CppSystem.Action _onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            _onComplete?.Invoke();
            return false;
        }
    }

    // RVA 0x59DA40, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetAttendanceReward), new Type[] { typeof(Il2CppSystem.Action<AttendanceReward>) })]
    public static class ManagerWebRequest_GetAttendanceReward_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<AttendanceReward> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(new AttendanceReward { gotReward = false, atdCount = 0, atdTime = 0 });
            return false;
        }
    }

    // RVA 0x59DD80, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetNewsList), new Type[] { typeof(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<NewsInfo>>), typeof(Il2CppSystem.Action) })]
    public static class ManagerWebRequest_GetNewsList_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<NewsInfo>> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(new Il2CppSystem.Collections.Generic.List<NewsInfo>());
            return false;
        }
    }

    // RVA 0x59E800, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetUserLicenseLevel), new Type[] { typeof(Il2CppSystem.Action<LicenseLevel>) })]
    public static class ManagerWebRequest_GetUserLicenseLevel_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<LicenseLevel> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(new LicenseLevel());
            return false;
        }
    }

    // RVA 0x59E1A0, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetUnityQuestProcess), new Type[] { typeof(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<NoticeInfo>>) })]
    public static class ManagerWebRequest_GetUnityQuestProcess_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<NoticeInfo>> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(new Il2CppSystem.Collections.Generic.List<NoticeInfo>());
            return false;
        }
    }

    // RVA 0x59E440, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetUnityQuestServer), new Type[] { typeof(Il2CppSystem.Action<URLPort>) })]
    public static class ManagerWebRequest_GetUnityQuestServer_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<URLPort> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(new URLPort { url = "127.0.0.1", port = 0 });
            return false;
        }
    }

    // RVA 0x59F690, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.RequestTravelModeArkkRewards), new Type[] { typeof(PlayResult), typeof(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<ArkkReward>>) })]
    public static class ManagerWebRequest_RequestTravelModeArkkRewards_Patch
    {
        public static bool Prefix(Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<ArkkReward>> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            onComplete?.Invoke(new Il2CppSystem.Collections.Generic.List<ArkkReward>());
            return false;
        }
    }

    // RVA 0x59E970, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.PostHighScoreData), new Type[] { typeof(PlayResult), typeof(Il2CppSystem.Action<int>) })]
    public static class ManagerWebRequest_PostHighScoreData_Patch
    {
        public static bool Prefix(PlayResult pr, Il2CppSystem.Action<int> onComplete)
        {
            if (!Core.BlockServerUpload) return true;
            Core.DebugLog("[OfflineBypass] Prevented legacy ManagerWebRequest.PostHighScoreData from sending to server.");
            onComplete?.Invoke(1);
            return false;
        }
    }

    // RVA 0x59DC80, ICF=1
    [HarmonyPatch(typeof(ManagerWebRequest), nameof(ManagerWebRequest.GetHighScoreList), new Type[] { typeof(TrackWithLevel), typeof(Il2CppSystem.Action<ScoreList>) })]
    public static class ManagerWebRequest_GetHighScoreList_Patch
    {
        public static bool Prefix(TrackWithLevel twl, Il2CppSystem.Action<ScoreList> callback)
        {
            if (!Core.BlockServerUpload) return true;
            callback?.Invoke(new ScoreList());
            return false;
        }
    }

    // RVA 0x70A1F0, ICF=1
    [HarmonyPatch(typeof(UpdateChecker), nameof(UpdateChecker.Check), new Type[] { typeof(Il2CppSystem.Action), typeof(Il2CppSystem.Action) })]
    public static class UpdateChecker_Check_Patch
    {
        public static bool Prefix(UpdateChecker __instance, Il2CppSystem.Action onLatestVersion)
        {
            if (!Core.BlockServerUpload) return true;
            try
            {
                if (__instance != null)
                {
                    __instance.checkedFlag = true;
                    if (__instance.gameObject != null)
                        __instance.gameObject.SetActive(false);
                }
                var ua = UserAccountModule.Instance;
                if (ua != null)
                    ua.runtimeCheck = new RuntimeCheck { isUpdateChecked = true };
            }
            catch { }
            onLatestVersion?.Invoke();
            return false;
        }
    }

    // RVA 0x5B2440, ICF=1
    [HarmonyPatch(typeof(UserAccountModule), nameof(UserAccountModule.CheckTutorial))]
    public static class UserAccountModule_CheckTutorial_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!Core.BlockServerUpload) return true;
            __result = false;
            return false;
        }
    }

    // RVA 0x5B3080, ICF=1 — ported from sxtg2 saver test DataPatches.LoadFavoritesPatch
    [HarmonyPatch(typeof(UserAccountModule), nameof(UserAccountModule.LoadFavorites))]
    public static class UserAccountModule_LoadFavorites_Patch
    {
        public static Exception Finalizer(Exception __exception, ref SXGT_Favorites __result)
        {
            if (__exception == null && __result != null) return null;
            __result = new SXGT_Favorites();
            return null;
        }
    }

    // RVA 0x59A390, ICF=1 — ported from sxtg2 saver test DataPatches.LoadUnlockCachePatch
    [HarmonyPatch(typeof(ItemHelper), nameof(ItemHelper.LoadUnlockCache))]
    public static class ItemHelper_LoadUnlockCache_Patch
    {
        public static bool Prefix(ref SXGT_UnlockCache __result)
        {
            if (!Core.UnlockAllTracks) return true;
            __result = new SXGT_UnlockCache
            {
                cachedHashList = new Il2CppSystem.Collections.Generic.List<string>()
            };
            return false;
        }
    }

    // RVA 0x5996D0, ICF=1 — ported from sxtg2 saver test DataPatches.SaveUnlockCachePatch
    [HarmonyPatch(typeof(ItemHelper), nameof(ItemHelper.CheckItemsUnlock))]
    public static class ItemHelper_CheckItemsUnlock_Patch
    {
        public static bool Prefix() => !Core.UnlockAllTracks;
    }

    // RVA 0x599CA0, ICF=1 — legacy ItemHelper ownership check
    [HarmonyPatch(typeof(ItemHelper), nameof(ItemHelper.CheckOwnedState), new Type[] { typeof(string) })]
    public static class ItemHelper_CheckOwnedState_String_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!Core.UnlockAllTracks) return true;
            __result = true;
            return false;
        }
    }

    // RVA 0x599DD0, ICF=1 — legacy ItemHelper ownership check
    [HarmonyPatch(typeof(ItemHelper), nameof(ItemHelper.CheckOwnedState), new Type[] { typeof(ICollectable) })]
    public static class ItemHelper_CheckOwnedState_Collectable_Patch
    {
        public static bool Prefix(ref bool __result)
        {
            if (!Core.UnlockAllTracks) return true;
            __result = true;
            return false;
        }
    }
}
