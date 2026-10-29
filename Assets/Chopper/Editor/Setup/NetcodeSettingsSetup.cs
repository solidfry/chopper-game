using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.NetCode;
using Unity.NetCode.Hybrid;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The project's global Netcode config (Project Settings → Multiplayer → Netcode). Two changes from the defaults.
    /// 1) The server may run up to <see cref="StepsPerFrame"/> full ticks in one slow frame. Netcode's default is one,
    /// so every frame longer than a tick (an editor at ~60 FPS with vsync, a hitch) merged ticks into one "batched"
    /// tick: merged input, less accurate physics, and the "Server Tick Batching" warning. Batching is now only for a
    /// real overload (more than 4 ticks behind).
    /// 2) The client receive queue holds <see cref="ClientReceiveQueue"/> packets, not 64: on a remote server (Edgegap,
    /// 29 Oct) the client's load stall let more than 64 snapshot packets arrive in one frame ("Receive queue is full").
    /// Creates the asset if missing; raises a smaller receive queue; never changes other values that exist.
    /// </summary>
    internal static class NetcodeSettingsSetup
    {
        private const string ConfigPath = "Assets/Chopper/Data/NetcodeConfig.asset";
        private const int StepsPerFrame = 4;
        private const int ClientReceiveQueue = 256;

        public static void Ensure()
        {
            var config = AssetDatabase.LoadAssetAtPath<NetCodeConfig>(ConfigPath);
            if (config == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                config = ScriptableObject.CreateInstance<NetCodeConfig>();
                config.ClientServerTickRate.MaxSimulationStepsPerFrame = StepsPerFrame;
                config.IsGlobalConfig = true;
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            if (config.ClientReceiveQueueCapacity < ClientReceiveQueue)
            {
                config.ClientReceiveQueueCapacity = ClientReceiveQueue;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }

            var settings = NetCodeClientAndServerSettings.instance;
            if (settings.GlobalNetCodeConfig == config) return;
            if (settings.GlobalNetCodeConfig != null)
            {
                Debug.LogWarning($"Setup: Netcode already uses the global config {settings.GlobalNetCodeConfig.name}; left as it is.",
                    settings.GlobalNetCodeConfig);
                return;
            }

            config.IsGlobalConfig = true;
            EditorUtility.SetDirty(config);
            settings.GlobalNetCodeConfig = config;
            // The settings file is a ScriptableSingleton; its Save is internal to Netcode.
            typeof(NetCodeClientAndServerSettings).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic, null,
                System.Type.EmptyTypes, null)?.Invoke(settings, null);
            AssetDatabase.SaveAssets();
        }

        public static IEnumerable<string> Problems()
        {
            var config = NetCodeClientAndServerSettings.instance.GlobalNetCodeConfig;
            if (config == null)
                yield return "No global Netcode config: slow frames batch server ticks. Press '1. Create / repair config'.";
            else if (config.ClientServerTickRate.MaxSimulationStepsPerFrame < 2)
                yield return $"{config.name}: Max Simulation Steps Per Frame is 1, so every slow frame batches server ticks (set 4).";
            if (config != null && config.ClientReceiveQueueCapacity < ClientReceiveQueue)
                yield return $"{config.name}: Client Receive Queue Capacity is {config.ClientReceiveQueueCapacity}; a remote server " +
                             $"can overflow it while the client loads (set {ClientReceiveQueue}, or press '1. Create / repair config').";
        }
    }
}
