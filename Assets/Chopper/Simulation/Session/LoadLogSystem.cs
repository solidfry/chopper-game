using System;
using System.Diagnostics;
using Unity.Entities;
using Unity.NetCode;
using Debug = UnityEngine.Debug;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// One log line every <see cref="IntervalSeconds"/> with the load of this world: players, ticks and frames per
    /// second, the worst frame, the process CPU (percent of one core) and memory; on a client also the RTT. Always on in
    /// a dedicated server (read it with Edgegap's container logs); on a client or a listen server with <c>-loadLog</c>.
    /// Lines start with "[Load]" so a log can be filtered.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    public partial class LoadLogSystem : SystemBase
    {
        public const double IntervalSeconds = 30.0;
        /// <summary>Frames longer than this count as hitches.</summary>
        private const double HitchSeconds = 0.05;

        private readonly Stopwatch _clock = new Stopwatch();
        private Process _process;
        private double _windowStart;
        private TimeSpan _cpuAtStart;
        private int _ticks;
        private int _frames;
        private int _lastFrame = -1;
        private double _lastFrameTime;
        private double _worstFrame;
        private int _hitches;

        protected override void OnCreate()
        {
#if UNITY_SERVER
            const bool dedicated = true;
#else
            var dedicated = UnityEngine.Application.isBatchMode;
#endif
            Enabled = (dedicated || ServerArgs.Flag("-loadLog")) && !World.IsThinClient();
        }

        protected override void OnStartRunning()
        {
            _process ??= Process.GetCurrentProcess();
            _clock.Restart();
            StartWindow();
        }

        protected override void OnUpdate()
        {
            var now = _clock.Elapsed.TotalSeconds;
            _ticks++;
            // The server runs this once per tick (up to 4 in a slow frame); count the frames apart.
            var frame = UnityEngine.Time.frameCount;
            if (frame != _lastFrame)
            {
                if (_lastFrame >= 0)
                {
                    var length = now - _lastFrameTime;
                    if (length > _worstFrame) _worstFrame = length;
                    if (length > HitchSeconds) _hitches++;
                }
                _lastFrame = frame;
                _lastFrameTime = now;
                _frames++;
            }

            var span = now - _windowStart;
            if (span < IntervalSeconds) return;

            _process.Refresh();
            var cpu = (_process.TotalProcessorTime - _cpuAtStart).TotalSeconds / span * 100.0;
            var players = SystemAPI.QueryBuilder().WithAll<NetworkId, NetworkStreamInGame>().Build().CalculateEntityCount();
            var line = $"[Load] [{World.Name}] players {players}, ticks/s {_ticks / span:0.0}, frames/s {_frames / span:0.0}, " +
                       $"worst frame {_worstFrame * 1000.0:0} ms, hitches (>{HitchSeconds * 1000.0:0} ms) {_hitches}, " +
                       $"CPU {cpu:0}% of a core, memory {Memory() / (1024 * 1024)} MB";
            if (World.IsClient() && SystemAPI.TryGetSingleton<NetworkSnapshotAck>(out var ack))
                line += $", RTT {ack.EstimatedRTT:0} ms";
            Debug.Log(line);
            // The same numbers in the match log (server), for later analysis.
            var matchLog = World.GetExistingSystemManaged<MatchLogSystem>();
            if (matchLog != null && matchLog.Enabled)
                matchLog.Write("load", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "\"players\":{0},\"ticks\":{1:0.0},\"frames\":{2:0.0},\"worstMs\":{3:0},\"hitches\":{4},\"cpu\":{5:0},\"memMB\":{6}",
                    players, _ticks / span, _frames / span, _worstFrame * 1000.0, _hitches, cpu, Memory() / (1024 * 1024)));
            StartWindow();
        }

        /// <summary>The working set; Unity's reserved memory where the runtime gives 0 (Mono on Windows).</summary>
        private long Memory()
        {
            var workingSet = _process.WorkingSet64;
            return workingSet > 0 ? workingSet : UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
        }

        private void StartWindow()
        {
            _windowStart = _clock.Elapsed.TotalSeconds;
            _cpuAtStart = _process.TotalProcessorTime;
            _ticks = 0;
            _frames = 0;
            _worstFrame = 0;
            _hitches = 0;
        }
    }
}
