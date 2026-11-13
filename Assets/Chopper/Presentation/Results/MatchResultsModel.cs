using System;
using System.Collections.Generic;
using Chopper.Presentation.Hud;
using Chopper.Presentation.Profile;
using Chopper.Simulation.Match;
using Chopper.Simulation.Progression;
using UnityEngine;

namespace Chopper.Presentation.Results
{
    /// <summary>One pilot on the results screen: their line and their awards.</summary>
    public sealed class ResultsPlayer
    {
        public int NetworkId;
        public int Place;
        public string Name;
        public string Chassis;
        public bool IsLocal;
        public PlayerMatchStats Stats;
        public readonly List<Award> Awards = new();
        /// <summary>For the UI assembly, which does not reference the simulation.</summary>
        public int Kills => Stats.Kills;
        public int Deaths => Stats.Deaths;
    }

    /// <summary>
    /// View model for the end-of-match screen. The server sends every player's stats when the match ends
    /// (<see cref="MatchStatsReceived"/>); after a short settle (they come as one message per player) this builds the
    /// awards (<see cref="MatchAwards"/>), the local XP (<see cref="MatchXp"/>), records the match in the profile
    /// (<see cref="ProfileService"/>) once, and raises <see cref="Built"/>. Names and places come from the <see cref="MatchHudModel"/> scoreboard.
    /// Edit-mode tests: MatchResultsModelTests.
    /// </summary>
    public sealed class MatchResultsModel
    {
        /// <summary>Seconds without a new stats message before the screen builds.</summary>
        public const float SettleSeconds = 0.25f;

        private readonly MatchHudModel _match;
        private readonly ProfileService _profile;
        private readonly XpRates _rates;
        private readonly Func<FlightShare> _localFlight;
        private readonly Dictionary<int, PlayerMatchStats> _stats = new();
        private readonly List<ResultsPlayer> _players = new();
        private readonly List<XpLine> _xpLines = new();
        private readonly List<Award> _scratch = new();
        private MatchPhase _phase;
        private int _winner;
        private float _settle = -1f;
        private bool _dismissed;

        /// <param name="settings">XP rates. Null = the default rates (tests).</param>
        /// <param name="localFlight">
        /// The local player's chassis class and pilot. Until the server counts flight time per chassis and pilot, the
        /// whole match counts for these. Null = no mastery XP.
        /// </param>
        public MatchResultsModel(MatchHudModel match, ProfileService profile, ProgressionSettings settings = null,
            Func<FlightShare> localFlight = null)
        {
            _match = match;
            _profile = profile;
            _rates = settings != null ? settings.Xp : XpRates.Default;
            _localFlight = localFlight;
        }

        /// <summary>Achievement ids this match unlocked (empty until built).</summary>
        public IReadOnlyList<string> NewAchievements { get; private set; } = Array.Empty<string>();

        /// <summary>The screen has data for this match (<see cref="Players"/>, <see cref="XpLines"/> and the rest).</summary>
        public bool Ready { get; private set; }

        /// <summary>Show the screen: results are ready and the player has not closed it.</summary>
        public bool Visible => Ready && _phase == MatchPhase.PostGame && !_dismissed;

        /// <summary>The data was (re)built: the view lays out the screen again and starts its count-up.</summary>
        public event Action Built;

        /// <summary><see cref="Visible"/> may have changed.</summary>
        public event Action VisibleChanged;

        /// <summary>"VICTORY", "DRAW" or "VIPER 3 WINS".</summary>
        public string Banner { get; private set; } = "";
        public bool Won { get; private set; }
        public bool Draw { get; private set; }
        /// <summary>All pilots, best first.</summary>
        public IReadOnlyList<ResultsPlayer> Players => _players;
        /// <summary>The local pilot's row, or null (a spectator, or no stats).</summary>
        public ResultsPlayer Local { get; private set; }
        public IReadOnlyList<XpLine> XpLines => _xpLines;
        public int XpGained { get; private set; }
        /// <summary>Total XP before this match's XP was added.</summary>
        public int XpBefore { get; private set; }
        public int XpAfter => XpBefore + XpGained;
        /// <summary>Kills x 100 plus damage dealt: the yellow card.</summary>
        public int Score { get; private set; }

        public static int ScoreOf(in PlayerMatchStats stats) => stats.Kills * 100 + Mathf.RoundToInt(stats.DamageDealt);

        public void SetMatch(MatchPhase phase, int winnerNetworkId)
        {
            var was = Visible;
            if (phase == MatchPhase.InProgress && _phase != MatchPhase.InProgress)
            {
                // A new match: the old results go.
                _stats.Clear();
                _players.Clear();
                NewAchievements = Array.Empty<string>();
                Local = null;
                Ready = false;
                _settle = -1f;
            }
            if (phase == MatchPhase.PostGame && _phase != MatchPhase.PostGame) _dismissed = false;
            _phase = phase;
            _winner = winnerNetworkId;
            if (Visible != was) VisibleChanged?.Invoke();
        }

        public void ApplyStats(int networkId, in PlayerMatchStats stats)
        {
            _stats[networkId] = stats;
            _settle = SettleSeconds;
        }

        /// <summary>PLAY AGAIN: hide the screen and wait for the next match.</summary>
        public void Dismiss()
        {
            if (_dismissed) return;
            var was = Visible;
            _dismissed = true;
            if (Visible != was) VisibleChanged?.Invoke();
        }

        /// <summary>Builds the screen once the stats stop coming. Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            if (_settle < 0f) return;
            _settle -= deltaTime;
            if (_settle > 0f) return;
            _settle = -1f;
            Build();
        }

        private void Build()
        {
            var was = Visible;
            var firstBuild = !Ready;
            _players.Clear();
            Local = null;
            foreach (var row in _match.Scoreboard)
            {
                if (!_stats.TryGetValue(row.NetworkId, out var stats)) continue;
                _players.Add(NewPlayer(row.NetworkId, row.Name, row.Chassis, row.IsLocal, stats));
            }
            // Stats for a player the scoreboard does not know (left in the last moment): still shown, last.
            foreach (var (networkId, stats) in _stats)
            {
                var known = false;
                foreach (var player in _players) known |= player.NetworkId == networkId;
                if (!known) _players.Add(NewPlayer(networkId, $"PILOT {networkId}", "", false, stats));
            }

            var place = 0;
            for (var i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                var tied = i > 0 && _players[i - 1].Stats.Kills == player.Stats.Kills && _players[i - 1].Stats.Deaths == player.Stats.Deaths;
                if (!tied) place = i + 1;
                player.Place = place;
                MatchAwards.For(player.NetworkId, _stats, _scratch);
                player.Awards.AddRange(_scratch);
                if (player.IsLocal) Local = player;
            }

            Won = Local != null && _winner == Local.NetworkId;
            Draw = _winner == 0;
            Banner = _match.BannerText;
            if (string.IsNullOrEmpty(Banner)) Banner = Draw ? "DRAW" : Won ? "VICTORY" : "MATCH OVER";

            if (Local != null)
            {
                var gained = MatchXp.Lines(Local.Stats, Local.Awards, Won, _xpLines, _rates);
                Score = ScoreOf(Local.Stats);
                // The XP is added once per match; a late stats message only redraws.
                if (firstBuild)
                {
                    XpBefore = _profile != null ? _profile.TotalXp : 0;
                    XpGained = gained;
                    if (_profile != null)
                    {
                        var flown = _localFlight != null ? new[] { _localFlight() } : null;
                        NewAchievements = _profile.RecordMatch(new MatchOutcome(Local.Stats, Local.Awards, Won, Draw, Local.Place,
                            _players.Count, gained, Score, _match.LocalPilotText, flown));
                    }
                }
            }
            else
            {
                _xpLines.Clear();
                XpGained = 0;
                Score = 0;
            }

            Ready = true;
            Built?.Invoke();
            if (Visible != was) VisibleChanged?.Invoke();
        }

        private static ResultsPlayer NewPlayer(int networkId, string name, string chassis, bool isLocal, in PlayerMatchStats stats) => new()
        {
            NetworkId = networkId,
            Name = name,
            Chassis = chassis ?? "",
            IsLocal = isLocal,
            Stats = stats,
        };
    }
}
