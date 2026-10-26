using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Chopper.Services.Matchmaking
{
    /// <summary>Matchmaker queues (Docs/MultiplayerOverhaulPlan.md section 4).</summary>
    public static class MatchQueues
    {
        public const string QuickPlay = "quickplay";
        public const string FreeForAll = "ffa";
        public const string TeamDeathmatch = "tdm";
        public const string CaptureTheNuke = "ctn";
    }

    public enum MatchmakingStatus
    {
        Searching,
        ServerStarting,
        Connecting,
    }

    /// <summary>What the player asks for. The loadout is sent so the server can validate it before spawn.</summary>
    public readonly struct MatchRequest
    {
        public string Queue { get; }
        public ushort ChassisId { get; }
        public IReadOnlyList<ushort> Modules { get; }

        public MatchRequest(string queue, ushort chassisId, IReadOnlyList<ushort> modules)
        {
            Queue = queue;
            ChassisId = chassisId;
            Modules = modules ?? Array.Empty<ushort>();
        }
    }

    /// <summary>Where to connect.</summary>
    public readonly struct MatchAssignment
    {
        public string Address { get; }
        public ushort Port { get; }
        /// <summary>Session id, kept for "Rejoin" after a crash or disconnect.</summary>
        public string SessionId { get; }

        public MatchAssignment(string address, ushort port, string sessionId)
        {
            Address = address;
            Port = port;
            SessionId = sessionId;
        }
    }

    public interface IMatchmakingService
    {
        /// <summary>Finds or starts a match. Cancel to leave the queue.</summary>
        UniTask<MatchAssignment> FindMatchAsync(MatchRequest request, IProgress<MatchmakingStatus> progress,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Dev only: "finds" the local server. Used until the Unity Matchmaker + Edgegap allocator is set up.
    /// </summary>
    public sealed class LocalMatchmakingService : IMatchmakingService
    {
        public const ushort DefaultPort = 7979;

        public UniTask<MatchAssignment> FindMatchAsync(MatchRequest request, IProgress<MatchmakingStatus> progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(MatchmakingStatus.Connecting);
            return UniTask.FromResult(new MatchAssignment("127.0.0.1", DefaultPort, sessionId: "local"));
        }
    }
}
