using System;
using GTSteamVRBodyTracking.Tools;
using UnityEngine.XR.Management;
using Valve.VR;

namespace GTSteamVRBodyTracking.Tracking
{
    internal enum SessionState
    {
        NotStarted,
        RuntimeMissing,
        NativeLibraryMissing,
        WaitingForSteamVR,
        Shared,
        Standalone,
        Error
    }

    /// <summary>
    /// Obtains an IVRSystem without disturbing the game:
    ///  * Shared: the game's XR loader (XRSDKOpenVR) already initialised OpenVR in this process;
    ///    OpenVR.System attaches to it lazily (verified in Unity.XR.OpenVR.dll). We never call Init/Shutdown.
    ///  * Standalone: only if the game's XR loader is NOT OpenVR, a VRApplication_Background session is
    ///    opened (it never launches SteamVR) and is shut down by us on unload.
    /// </summary>
    internal sealed class OpenVRSession
    {
        private const float RetrySeconds = 3f;
        private const float StandaloneDelaySeconds = 15f;

        private readonly bool _allowStandalone;
        private readonly float _createdAt;
        private float _nextAttempt;
        private bool _ownsSession;

        public SessionState State { get; private set; } = SessionState.NotStarted;
        public string StatusText { get; private set; } = "Not started";
        public CVRSystem System { get; private set; }
        public bool IsConnected => System != null && (State == SessionState.Shared || State == SessionState.Standalone);
        public int Revision { get; private set; }

        public OpenVRSession(bool allowStandalone, float now)
        {
            _allowStandalone = allowStandalone;
            _createdAt = now;
        }

        public void Update(float now)
        {
            if (IsConnected)
            {
                // Re-validate cheaply: the getter compares init tokens and drops stale interfaces.
                CVRSystem current = null;
                try { current = OpenVR.System; }
                catch (Exception e) { ModLog.Exception("session-check", e, "OpenVR session check failed"); }

                if (current == null)
                {
                    ModLog.Warn("OpenVR session lost; trackers marked disconnected. Retrying.");
                    ReleaseOwned();
                    System = null;
                    SetState(SessionState.WaitingForSteamVR, "SteamVR session lost; retrying");
                    _nextAttempt = now + RetrySeconds;
                }
                else
                {
                    System = current;
                }
                return;
            }

            if (now < _nextAttempt) return;
            _nextAttempt = now + RetrySeconds;
            TryConnect(now);
        }

        private void TryConnect(float now)
        {
            try
            {
                if (!OpenVR.IsRuntimeInstalled())
                {
                    SetState(SessionState.RuntimeMissing, "SteamVR runtime not installed");
                    _nextAttempt = now + 30f;
                    return;
                }

                CVRSystem shared = OpenVR.System;
                if (shared != null)
                {
                    System = shared;
                    SetState(SessionState.Shared, "Connected (game's SteamVR session)");
                    ModLog.Info("Attached to the game's OpenVR session.");
                    return;
                }

                bool loaderIsOpenVR = IsOpenVRLoaderActive(out string loaderName);
                if (!_allowStandalone || loaderIsOpenVR || now - _createdAt < StandaloneDelaySeconds)
                {
                    SetState(SessionState.WaitingForSteamVR, loaderIsOpenVR
                        ? "Waiting for the game's SteamVR session"
                        : $"Waiting for SteamVR (XR loader: {loaderName})");
                    return;
                }

                EVRInitError error = EVRInitError.None;
                CVRSystem standalone = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background, string.Empty);
                if (error == EVRInitError.None && standalone != null)
                {
                    _ownsSession = true;
                    System = standalone;
                    SetState(SessionState.Standalone, "Connected (background SteamVR session)");
                    ModLog.Info($"Opened a background OpenVR session (game XR loader: {loaderName}).");
                }
                else
                {
                    SetState(SessionState.WaitingForSteamVR, $"SteamVR not available ({error})");
                    ModLog.WarnThrottled("standalone-init", $"Background OpenVR init failed: {error}. Is SteamVR running?", 60f);
                }
            }
            catch (DllNotFoundException e)
            {
                SetState(SessionState.NativeLibraryMissing, "openvr_api.dll could not be loaded");
                ModLog.ErrorOnce("openvr-dll", "openvr_api.dll could not be loaded: " + e.Message);
                _nextAttempt = now + 60f;
            }
            catch (EntryPointNotFoundException e)
            {
                SetState(SessionState.NativeLibraryMissing, "Incompatible openvr_api.dll");
                ModLog.ErrorOnce("openvr-entry", "openvr_api.dll is missing an expected export: " + e.Message);
                _nextAttempt = now + 60f;
            }
            catch (Exception e)
            {
                SetState(SessionState.Error, "SteamVR error: " + e.GetType().Name);
                ModLog.Exception("session-connect", e, "OpenVR connection attempt failed");
            }
        }

        private static bool IsOpenVRLoaderActive(out string loaderName)
        {
            try
            {
                XRLoader loader = XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null
                    ? XRGeneralSettings.Instance.Manager.activeLoader
                    : null;
                if (loader == null)
                {
                    loaderName = "none";
                    return false;
                }
                loaderName = loader.GetType().Name;
                return loader is Unity.XR.OpenVR.OpenVRLoader;
            }
            catch (Exception)
            {
                // If we cannot tell, assume the game owns OpenVR so we never open a competing session.
                loaderName = "unknown";
                return true;
            }
        }

        private void SetState(SessionState state, string text)
        {
            if (State == state && StatusText == text) return;
            State = state;
            StatusText = text;
            Revision++;
        }

        private void ReleaseOwned()
        {
            if (!_ownsSession) return;
            _ownsSession = false;
            try { OpenVR.Shutdown(); }
            catch (Exception e) { ModLog.Warn("OpenVR.Shutdown failed: " + e.Message); }
        }

        public void Shutdown()
        {
            ReleaseOwned();
            System = null;
            SetState(SessionState.NotStarted, "Stopped");
        }
    }
}
