using System;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Networking;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// When a dedicated server stops: no player within <see cref="FirstPlayerTimeout"/> seconds of start, or nobody
    /// left for <see cref="EmptyTimeout"/> seconds after the last player went. 0 or less = never. Pure, so edit-mode
    /// tests can run it.
    /// </summary>
    public struct ServerIdlePolicy
    {
        public const float DefaultFirstPlayerTimeout = 300f;
        public const float DefaultEmptyTimeout = 120f;

        public float FirstPlayerTimeout;
        public float EmptyTimeout;

        private bool _hadPlayer;
        private bool _occupied;
        private double _emptySince;

        public ServerIdlePolicy(float firstPlayerTimeout, float emptyTimeout, double startTime)
        {
            FirstPlayerTimeout = firstPlayerTimeout;
            EmptyTimeout = emptyTimeout;
            _hadPlayer = false;
            _occupied = false;
            _emptySince = startTime;
        }

        /// <summary>Call once per update with the connected players; true when the server should stop.</summary>
        public bool ShouldStop(int players, double now)
        {
            if (players > 0)
            {
                _hadPlayer = true;
                _occupied = true;
                return false;
            }
            if (_occupied)
            {
                _occupied = false;
                _emptySince = now;
            }
            var limit = _hadPlayer ? EmptyTimeout : FirstPlayerTimeout;
            return limit > 0f && now - _emptySince >= limit;
        }
    }

    /// <summary>
    /// Dedicated server only (<c>UNITY_SERVER</c>). Stops an idle server (<see cref="ServerIdlePolicy"/>) so a hosted
    /// deploy does not run, and cost money, with nobody on it. On Edgegap it first asks Edgegap to end the deployment
    /// (<c>ARBITRIUM_DELETE_URL</c>, <c>ARBITRIUM_DELETE_TOKEN</c>); a plain exit could be restarted by the app
    /// version's restart policy. Then the process quits. Timeouts: <c>-idleStart</c> and <c>-idleEmpty</c> (seconds)
    /// on the command line, else the defaults.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class ServerLifecycleSystem : SystemBase
    {
        private const double SelfStopTimeoutSeconds = 10.0;

        private ServerIdlePolicy _policy;
        private UnityWebRequest _selfStop;
        private double _stopStarted = -1;

        protected override void OnCreate()
        {
#if !UNITY_SERVER
            Enabled = false;
#endif
            _policy = new ServerIdlePolicy(
                ServerArgs.Float("-idleStart", ServerIdlePolicy.DefaultFirstPlayerTimeout),
                ServerArgs.Float("-idleEmpty", ServerIdlePolicy.DefaultEmptyTimeout),
                0.0);
        }

        protected override void OnUpdate()
        {
            var now = SystemAPI.Time.ElapsedTime;
            if (_stopStarted >= 0)
            {
                if (_selfStop != null && !_selfStop.isDone && now - _stopStarted < SelfStopTimeoutSeconds) return;
                if (_selfStop != null)
                    Debug.Log($"[Server] Edgegap self-stop: {(_selfStop.isDone ? _selfStop.result.ToString() : "timed out")} " +
                              $"({_selfStop.responseCode}). Quitting.");
                _selfStop?.Dispose();
                _selfStop = null;
                Enabled = false;
                Application.Quit(0);
                return;
            }

            var players = SystemAPI.QueryBuilder().WithAll<NetworkId>().Build().CalculateEntityCount();
            if (!_policy.ShouldStop(players, now)) return;

            _stopStarted = now;
            var url = Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_URL");
            var token = Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_TOKEN");
            Debug.Log($"[Server] Idle (no players), stopping after {now:0} s." +
                      (string.IsNullOrEmpty(url) ? "" : " Asking Edgegap to end the deployment."));
            if (string.IsNullOrEmpty(url)) return;

            _selfStop = UnityWebRequest.Delete(url);
            if (!string.IsNullOrEmpty(token)) _selfStop.SetRequestHeader("Authorization", token);
            _selfStop.SendWebRequest();
        }

        protected override void OnDestroy() => _selfStop?.Dispose();
    }

    /// <summary>Server settings from the command line and the host's environment (no state).</summary>
    public static class ServerArgs
    {
        /// <summary>The value after <paramref name="name"/> on the command line, else <paramref name="fallback"/>.</summary>
        public static string String(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        public static float Float(string name, float fallback) =>
            float.TryParse(String(name), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

        /// <summary>
        /// The game port: <c>-port</c>, else Edgegap's internal port for the port named "gameport"
        /// (<c>ARBITRIUM_PORT_GAMEPORT_INTERNAL</c>), else <paramref name="fallback"/>.
        /// </summary>
        public static ushort Port(ushort fallback)
        {
            if (ushort.TryParse(String("-port"), out var port) && port > 0) return port;
            if (ushort.TryParse(Environment.GetEnvironmentVariable("ARBITRIUM_PORT_GAMEPORT_INTERNAL"), out port) && port > 0)
                return port;
            return fallback;
        }
    }
}
