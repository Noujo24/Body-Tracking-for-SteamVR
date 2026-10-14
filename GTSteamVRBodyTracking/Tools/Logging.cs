using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace GTSteamVRBodyTracking.Tools
{
    /// <summary>
    /// Thin wrapper over the BepInEx log source with per-key throttling so that
    /// per-frame failure paths can never flood LogOutput.log.
    /// </summary>
    internal static class ModLog
    {
        private static ManualLogSource _log;
        private static readonly Dictionary<string, float> LastLogTime = new Dictionary<string, float>();
        private static readonly HashSet<string> OnceKeys = new HashSet<string>();

        public static void Init(ManualLogSource log) => _log = log;

        public static void Info(string message) => _log?.LogInfo(message);
        public static void Message(string message) => _log?.LogMessage(message);
        public static void Warn(string message) => _log?.LogWarning(message);
        public static void Error(string message) => _log?.LogError(message);
        public static void Debug(string message) => _log?.LogDebug(message);

        /// <summary>Logs a warning at most once per <paramref name="intervalSeconds"/> for a given key.</summary>
        public static void WarnThrottled(string key, string message, float intervalSeconds = 30f)
        {
            if (ShouldLog(key, intervalSeconds)) _log?.LogWarning(message);
        }

        /// <summary>Logs an error only the first time a key is seen this session.</summary>
        public static void ErrorOnce(string key, string message)
        {
            if (OnceKeys.Add(key)) _log?.LogError(message);
        }

        public static void InfoOnce(string key, string message)
        {
            if (OnceKeys.Add(key)) _log?.LogInfo(message);
        }

        /// <summary>Logs an exception with stack trace the first time, then a short throttled line.</summary>
        public static void Exception(string key, Exception e, string context)
        {
            if (OnceKeys.Add("exc:" + key))
            {
                _log?.LogError($"{context}: {e}");
                MarkLogged(key);
            }
            else if (ShouldLog(key, 30f))
            {
                _log?.LogError($"{context} (repeated): {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool ShouldLog(string key, float interval)
        {
            float now = Time.realtimeSinceStartup;
            if (LastLogTime.TryGetValue(key, out float last) && now - last < interval) return false;
            LastLogTime[key] = now;
            return true;
        }

        private static void MarkLogged(string key) => LastLogTime[key] = Time.realtimeSinceStartup;
    }
}
