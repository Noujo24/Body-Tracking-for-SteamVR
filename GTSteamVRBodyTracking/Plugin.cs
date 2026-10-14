using System;
using BepInEx;
using GTSteamVRBodyTracking.Behaviours;
using GTSteamVRBodyTracking.Tools;
using UnityEngine;

namespace GTSteamVRBodyTracking
{
    [BepInPlugin(Constants.Guid, Constants.Name, Constants.Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance { get; private set; }

        private GameObject _root;

        private void Awake()
        {
            if (Instance != null)
            {
                Logger.LogWarning("A second GTSteamVRBodyTracking instance was created; destroying the duplicate.");
                Destroy(this);
                return;
            }

            Instance = this;
            ModLog.Init(Logger);
            ModLog.Info($"{Constants.Name} {Constants.Version} starting (Unity {Application.unityVersion}).");

            try
            {
                _root = new GameObject(Constants.Name);
                DontDestroyOnLoad(_root);
                _root.AddComponent<ModRuntime>().Initialize(Config);
            }
            catch (Exception e)
            {
                ModLog.Error($"Startup failed; the mod is inactive. {e}");
                Shutdown();
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Shutdown();
            Instance = null;
        }

        private void OnApplicationQuit() => Shutdown();

        private void Shutdown()
        {
            if (_root != null)
            {
                // ModRuntime.OnDestroy releases patches, OpenVR resources and every created object.
                Destroy(_root);
                _root = null;
                ModLog.Info($"{Constants.Name} shut down.");
            }
        }
    }
}
