using System;
using GTSteamVRBodyTracking.Avatar;
using GTSteamVRBodyTracking.Tools;
using HarmonyLib;

namespace GTSteamVRBodyTracking.Patches
{
    /// <summary>
    /// Scopes the avatar overrides to GorillaIKMgr.CopyInput: applied in the prefix, always restored in the
    /// finalizer, so network serialization and GTPlayer never see them (Docs/GAME_INTEGRATION.md §5.3–5.4).
    /// </summary>
    [HarmonyPatch(typeof(GorillaIKMgr), "CopyInput")]
    internal static class CopyInputPatch
    {
        private static void Prefix()
        {
            try { AvatarIntegration.Active?.Apply(); }
            catch (Exception e)
            {
                ModLog.Exception("avatar-apply", e, "Avatar override failed");
                AvatarIntegration.Active?.Restore();
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            try { AvatarIntegration.Active?.Restore(); }
            catch (Exception e) { ModLog.Exception("avatar-restore", e, "Avatar restore failed"); }
            return __exception;
        }
    }
}
