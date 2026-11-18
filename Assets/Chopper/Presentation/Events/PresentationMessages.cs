using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using UnityEngine;

namespace Chopper.Presentation.Events
{
    // Messages from simulation to presentation. Published only by PresentationEventSystem (one place reads RPCs and
    // event entities); feature code subscribes through PresentationEvents. Add a message here and register it in
    // PresentationEvents when a new simulation event needs presentation.

    /// <summary>The local player's network id is known or changed.</summary>
    public readonly struct LocalPlayerIdentified
    {
        public readonly int NetworkId;
        public LocalPlayerIdentified(int networkId) => NetworkId = networkId;
    }

    /// <summary>The server confirmed one of our hits.</summary>
    public readonly struct HitConfirmed
    {
        public readonly float Damage;
        public readonly bool Killed;
        public HitConfirmed(float damage, bool killed) { Damage = damage; Killed = killed; }
    }

    /// <summary>We took damage.</summary>
    public readonly struct DamageTaken
    {
        public readonly float Amount;
        /// <summary>False for crash and bounds damage.</summary>
        public readonly bool HasSource;
        public readonly Vector3 SourcePosition;
        public readonly DamageKind Kind;
        /// <summary>Crashes and landings: impact speed (m/s). 0 for other hits.</summary>
        public readonly float ImpactSpeed;
        /// <summary>The attacker's pilot jams HUDs (HEX).</summary>
        public readonly bool Jam;
        public DamageTaken(float amount, bool hasSource, Vector3 sourcePosition, DamageKind kind, float impactSpeed, bool jam = false)
        {
            Jam = jam;
            Amount = amount;
            HasSource = hasSource;
            SourcePosition = sourcePosition;
            Kind = kind;
            ImpactSpeed = impactSpeed;
        }
    }

    public readonly struct ScoreChanged
    {
        public readonly int NetworkId;
        public readonly int Kills;
        public readonly int Deaths;
        /// <summary>The player's team (0 in Free For All).</summary>
        public readonly byte Team;
        public ScoreChanged(int networkId, int kills, int deaths, byte team = 0) { NetworkId = networkId; Kills = kills; Deaths = deaths; Team = team; }
    }

    /// <summary>Team modes: one team's kills.</summary>
    public readonly struct TeamScoreChanged
    {
        public readonly byte Team;
        public readonly int Score;
        public TeamScoreChanged(byte team, int score) { Team = team; Score = score; }
    }

    public readonly struct PlayerLeft
    {
        public readonly int NetworkId;
        public PlayerLeft(int networkId) => NetworkId = networkId;
    }

    public readonly struct PlayerKilled
    {
        /// <summary>0 = no killer (crash, bounds).</summary>
        public readonly int KillerNetworkId;
        public readonly int VictimNetworkId;
        public PlayerKilled(int killer, int victim) { KillerNetworkId = killer; VictimNetworkId = victim; }
    }

    public readonly struct MatchStateChanged
    {
        public readonly MatchPhase Phase;
        public readonly float TimeRemaining;
        public readonly int WinnerNetworkId;
        public readonly MatchMode Mode;
        /// <summary>Team modes: number of teams (0 in Free For All).</summary>
        public readonly int TeamCount;
        /// <summary>Team modes: the winning team; 0 = draw or still running.</summary>
        public readonly byte WinnerTeam;
        public MatchStateChanged(MatchPhase phase, float timeRemaining, int winner, MatchMode mode = MatchMode.FreeForAll,
            int teamCount = 0, byte winnerTeam = 0)
        {
            Phase = phase;
            TimeRemaining = timeRemaining;
            WinnerNetworkId = winner;
            Mode = mode;
            TeamCount = teamCount;
            WinnerTeam = winnerTeam;
        }
    }

    /// <summary>The match ended: one player's stats from the server (one message per player).</summary>
    public readonly struct MatchStatsReceived
    {
        public readonly int NetworkId;
        public readonly PlayerMatchStats Stats;
        public MatchStatsReceived(int networkId, PlayerMatchStats stats) { NetworkId = networkId; Stats = stats; }
    }

    /// <summary>A rocket was launched: our own (predicted, at once) or another player's (from the server).</summary>
    public readonly struct RocketLaunched
    {
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly float Lifetime;
        public readonly int ShooterNetworkId;
        /// <summary>Path and fuse, as on the server (<see cref="RocketFlight"/>). All 0 = a straight contact rocket.</summary>
        public readonly float Gravity;
        public readonly float Drag;
        public readonly float ProximityFuse;
        public readonly TargetClass FuseTargets;
        /// <summary>Blast size, metres: the visual's size follows it. 0 = not given (standard size).</summary>
        public readonly float SplashRadius;
        public RocketLaunched(Vector3 position, Vector3 velocity, float lifetime, int shooter, float gravity = 0f, float drag = 0f,
            float proximityFuse = 0f, TargetClass fuseTargets = TargetClass.None, float splashRadius = 0f)
        {
            SplashRadius = splashRadius;
            Position = position;
            Velocity = velocity;
            Lifetime = lifetime;
            ShooterNetworkId = shooter;
            Gravity = gravity;
            Drag = drag;
            ProximityFuse = proximityFuse;
            FuseTargets = fuseTargets;
        }
    }

    /// <summary>A guided missile exploded here (the server removed its ghost).</summary>
    public readonly struct MissileDetonated
    {
        public readonly Vector3 Position;
        public MissileDetonated(Vector3 position) => Position = position;
    }

    /// <summary>
    /// The server says a ghost with health (a chopper, a training target) was hit here. The point and normal are in
    /// the victim's own space, so effects can stay on the hull.
    /// </summary>
    public readonly struct ChopperHit
    {
        /// <summary><c>GhostInstance.ghostId</c> of the victim.</summary>
        public readonly int GhostId;
        public readonly Vector3 LocalPoint;
        public readonly Vector3 LocalNormal;
        public readonly float Amount;
        public readonly DamageKind Kind;
        public ChopperHit(int ghostId, Vector3 localPoint, Vector3 localNormal, float amount, DamageKind kind)
        {
            GhostId = ghostId;
            LocalPoint = localPoint;
            LocalNormal = localNormal;
            Amount = amount;
            Kind = kind;
        }
    }

    /// <summary>
    /// This client's link to the server closed: lost, timed out, refused, or the server stopped. <see cref="WasConnected"/>
    /// is false when the connect itself failed (no server at the address, or a different game version).
    /// </summary>
    public readonly struct ConnectionLost
    {
        public readonly Unity.NetCode.NetworkStreamDisconnectReason Reason;
        public readonly bool WasConnected;
        public ConnectionLost(Unity.NetCode.NetworkStreamDisconnectReason reason, bool wasConnected)
        {
            Reason = reason;
            WasConnected = wasConnected;
        }
    }

    /// <summary>The server gave the local player XP just now (the Battlefield-style popup).</summary>
    public readonly struct XpAwarded
    {
        public readonly XpReason Reason;
        public readonly int Amount;
        /// <summary>The victim (kill, assist) or the missile's shooter. 0 = none.</summary>
        public readonly int OtherNetworkId;
        /// <summary>Kills: the damage XP done to this victim, shown as its own line.</summary>
        public readonly int DamageXp;
        public XpAwarded(XpReason reason, int amount, int otherNetworkId, int damageXp)
        {
            Reason = reason;
            Amount = amount;
            OtherNetworkId = otherNetworkId;
            DamageXp = damageXp;
        }
    }
}
