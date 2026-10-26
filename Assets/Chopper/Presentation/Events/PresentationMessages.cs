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
        public DamageTaken(float amount, bool hasSource, Vector3 sourcePosition) { Amount = amount; HasSource = hasSource; SourcePosition = sourcePosition; }
    }

    public readonly struct ScoreChanged
    {
        public readonly int NetworkId;
        public readonly int Kills;
        public readonly int Deaths;
        public ScoreChanged(int networkId, int kills, int deaths) { NetworkId = networkId; Kills = kills; Deaths = deaths; }
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
        public MatchStateChanged(MatchPhase phase, float timeRemaining, int winner) { Phase = phase; TimeRemaining = timeRemaining; WinnerNetworkId = winner; }
    }

    /// <summary>A rocket was launched: our own (predicted, at once) or another player's (from the server).</summary>
    public readonly struct RocketLaunched
    {
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly float Lifetime;
        public readonly int ShooterNetworkId;
        public RocketLaunched(Vector3 position, Vector3 velocity, float lifetime, int shooter)
        {
            Position = position;
            Velocity = velocity;
            Lifetime = lifetime;
            ShooterNetworkId = shooter;
        }
    }
}
