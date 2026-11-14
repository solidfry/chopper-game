using System;
using System.Collections.Generic;
using System.Text;
using Chopper.Simulation.Match;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Chopper.Presentation.Hud
{
    /// <summary>One player on the scoreboard. <see cref="MatchHudModel.Scoreboard"/> holds them best first.</summary>
    public readonly struct ScoreboardRow
    {
        public readonly int NetworkId;
        /// <summary>1 = first. Players with the same kills and deaths share a place.</summary>
        public readonly int Place;
        /// <summary>"VIPER 3", or "PILOT 3" without a pilot. Never "YOU": <see cref="IsLocal"/> marks the own row.</summary>
        public readonly string Name;
        /// <summary>Chassis display name ("GUNSHIP"), "" until the chopper is seen.</summary>
        public readonly string Chassis;
        public readonly int Kills;
        public readonly int Deaths;
        public readonly bool IsLocal;

        public ScoreboardRow(int networkId, int place, string name, string chassis, int kills, int deaths, bool isLocal)
        {
            NetworkId = networkId;
            Place = place;
            Name = name;
            Chassis = chassis;
            Kills = kills;
            Deaths = deaths;
            IsLocal = isLocal;
        }
    }

    /// <summary>
    /// View model for the match part of the HUD: timer, scores, kill feed, the scoreboard and the end-of-match banner.
    /// <see cref="Feedback.MatchFeedback"/> fills it from match messages; UI Toolkit reads it through data binding.
    /// </summary>
    public sealed class MatchHudModel : INotifyBindablePropertyChanged
    {
        public const int MaxKillFeedEntries = 5;
        public const float KillFeedEntrySeconds = 5f;

        private readonly Dictionary<int, (int Kills, int Deaths)> _scores = new();
        private readonly List<(string Text, float Remaining)> _killFeed = new();
        private readonly StringBuilder _builder = new();

        private int _localNetworkId;
        private readonly Dictionary<int, string> _callSigns = new();
        /// <summary>Full names that replace "CALLSIGN id" (server bots).</summary>
        private readonly Dictionary<int, string> _names = new();
        private readonly Dictionary<int, string> _chassis = new();
        private readonly List<ScoreboardRow> _rows = new();
        private bool _scoreboardHeld;
        private bool _scoreboardVisible;
        private string _localPilotText = "";
        private MatchPhase _phase;
        private float _timeRemaining;
        private int _winnerNetworkId;

        private string _timerText = "00:00";
        private string _localScoreText = "0 K / 0 D";
        private string _leaderText = "";
        private string _killFeedText = "";
        private string _bannerText = "";
        private bool _bannerVisible;
        private string _deathAlertText = "";
        private bool _deathAlertVisible;

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        /// <summary>A row was added, removed or changed (<see cref="Scoreboard"/>).</summary>
        public event Action ScoreboardChanged;

        /// <summary>All players, best first: most kills, then fewest deaths, then the lowest id.</summary>
        public IReadOnlyList<ScoreboardRow> Scoreboard => _rows;

        /// <summary>Shown while the scoreboard key is held (Tab / gamepad View) and always after the match.</summary>
        [CreateProperty]
        public bool ScoreboardVisible
        {
            get => _scoreboardVisible;
            private set => Set(ref _scoreboardVisible, value, nameof(ScoreboardVisible));
        }

        /// <summary>"FREE FOR ALL // 4 PILOTS".</summary>
        [CreateProperty]
        public string ScoreboardTitle
        {
            get => _scoreboardTitle;
            private set => Set(ref _scoreboardTitle, value, nameof(ScoreboardTitle));
        }

        private string _scoreboardTitle = "FREE FOR ALL";

        /// <summary>The view sets this from the scoreboard action (false while a menu is open).</summary>
        public void SetScoreboardHeld(bool held)
        {
            _scoreboardHeld = held;
            RefreshScoreboardVisible();
        }

        /// <summary>A player's chassis name for the scoreboard (<see cref="PilotNamesSystem"/>).</summary>
        public void SetChassis(int networkId, string chassis)
        {
            chassis ??= "";
            if (_chassis.TryGetValue(networkId, out var known) && known == chassis) return;
            _chassis[networkId] = chassis;
            RefreshScores();
        }

        [CreateProperty]
        public string TimerText
        {
            get => _timerText;
            private set => Set(ref _timerText, value, nameof(TimerText));
        }

        [CreateProperty]
        public string LocalScoreText
        {
            get => _localScoreText;
            private set => Set(ref _localScoreText, value, nameof(LocalScoreText));
        }

        /// <summary>Player with the most kills, empty before the first kill.</summary>
        [CreateProperty]
        public string LeaderText
        {
            get => _leaderText;
            private set => Set(ref _leaderText, value, nameof(LeaderText));
        }

        /// <summary>Newest kill at the bottom, one per line.</summary>
        [CreateProperty]
        public string KillFeedText
        {
            get => _killFeedText;
            private set => Set(ref _killFeedText, value, nameof(KillFeedText));
        }

        [CreateProperty]
        public string BannerText
        {
            get => _bannerText;
            private set => Set(ref _bannerText, value, nameof(BannerText));
        }

        [CreateProperty]
        public bool BannerVisible
        {
            get => _bannerVisible;
            private set => Set(ref _bannerVisible, value, nameof(BannerVisible));
        }

        /// <summary>"YOU CRASHED" or "SHOT DOWN BY ...": from your death until you respawn. With a red tint on the screen.</summary>
        [CreateProperty]
        public string DeathAlertText
        {
            get => _deathAlertText;
            private set => Set(ref _deathAlertText, value, nameof(DeathAlertText));
        }

        [CreateProperty]
        public bool DeathAlertVisible
        {
            get => _deathAlertVisible;
            private set => Set(ref _deathAlertVisible, value, nameof(DeathAlertVisible));
        }

        /// <summary>You have a chopper again.</summary>
        public void ClearDeathAlert() => DeathAlertVisible = false;

        public void SetLocalPlayer(int networkId)
        {
            if (_localNetworkId == networkId) return;
            _localNetworkId = networkId;
            LocalPilotText = _callSigns.TryGetValue(networkId, out var sign) ? sign : "";
            RefreshScores();
            RefreshBanner();
        }

        public void ApplyScore(int networkId, int kills, int deaths)
        {
            _scores[networkId] = (kills, deaths);
            RefreshScores();
        }

        public void RemovePlayer(int networkId)
        {
            // Call signs and chassis stay: PilotNamesSystem sends only changes, and the rows come from the scores.
            if (_scores.Remove(networkId))
                RefreshScores();
        }

        /// <param name="killerNetworkId">0 when nobody gets the kill (crash, bounds).</param>
        public void AddKill(int killerNetworkId, int victimNetworkId)
        {
            var victim = PilotName(victimNetworkId);
            var crashed = killerNetworkId == 0 || killerNetworkId == victimNetworkId;
            var text = crashed
                ? $"{victim} CRASHED"
                : $"{PilotName(killerNetworkId)} > {victim}";

            if (victimNetworkId == _localNetworkId && _phase != MatchPhase.PostGame)
            {
                DeathAlertText = crashed ? "YOU CRASHED" : $"SHOT DOWN BY {PilotName(killerNetworkId)}";
                DeathAlertVisible = true;
            }

            _killFeed.Add((text, KillFeedEntrySeconds));
            if (_killFeed.Count > MaxKillFeedEntries)
                _killFeed.RemoveAt(0);
            RefreshKillFeed();
        }

        public void SetMatch(MatchPhase phase, float timeRemaining, int winnerNetworkId)
        {
            _phase = phase;
            _timeRemaining = timeRemaining;
            _winnerNetworkId = winnerNetworkId;
            RefreshTimer();
            RefreshBanner();
        }

        /// <summary>Counts down the timer and the kill feed. Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            _timeRemaining = MathF.Max(0f, _timeRemaining - deltaTime);
            RefreshTimer();

            var removed = false;
            for (var i = _killFeed.Count - 1; i >= 0; i--)
            {
                var entry = _killFeed[i];
                entry.Remaining -= deltaTime;
                if (entry.Remaining <= 0f)
                {
                    _killFeed.RemoveAt(i);
                    removed = true;
                }
                else
                {
                    _killFeed[i] = entry;
                }
            }
            if (removed) RefreshKillFeed();
        }

        /// <summary>"YOU" for the local player; else <see cref="PlayerName"/>.</summary>
        private string PilotName(int networkId) => networkId == _localNetworkId ? "YOU" : PlayerName(networkId);

        /// <summary>
        /// "VIPER 3" with a pilot (several players may fly the same pilot); else "PILOT 3". The one place names are made:
        /// Steam names come in here later (plan section 6, "NAME [VIPER]").
        /// </summary>
        private string PlayerName(int networkId) =>
            _names.TryGetValue(networkId, out var name) && !string.IsNullOrEmpty(name) ? name
            : _callSigns.TryGetValue(networkId, out var sign) && !string.IsNullOrEmpty(sign) ? $"{sign} {networkId}"
            : $"PILOT {networkId}";

        /// <summary>
        /// A full name for a player, used in place of the call sign and id (a server bot: "[BOT] RAZOR";
        /// <see cref="PilotNamesSystem"/>). Empty = back to the call sign.
        /// </summary>
        public void SetPlayerName(int networkId, string name)
        {
            name ??= "";
            if (_names.TryGetValue(networkId, out var known) && known == name) return;
            _names[networkId] = name;
            RefreshScores();
        }

        /// <summary>
        /// A player's pilot call sign (from their chopper's loadout; <see cref="PilotNamesSystem"/>). Empty = no pilot.
        /// The local player's own call sign shows under the score.
        /// </summary>
        public void SetCallSign(int networkId, string callSign)
        {
            callSign ??= "";
            if (_callSigns.TryGetValue(networkId, out var known) && known == callSign) return;
            _callSigns[networkId] = callSign;
            if (networkId == _localNetworkId) LocalPilotText = callSign;
            RefreshScores();
        }

        /// <summary>The local player's call sign ("" without a pilot).</summary>
        [CreateProperty]
        public string LocalPilotText
        {
            get => _localPilotText;
            private set => Set(ref _localPilotText, value, nameof(LocalPilotText));
        }

        private void RefreshTimer()
        {
            var seconds = (int)MathF.Ceiling(_timeRemaining);
            TimerText = $"{seconds / 60:00}:{seconds % 60:00}";
        }

        private void RefreshScores()
        {
            _scores.TryGetValue(_localNetworkId, out var local);
            LocalScoreText = $"{local.Kills} K / {local.Deaths} D";

            var leader = 0;
            var leaderKills = 0;
            foreach (var (networkId, score) in _scores)
            {
                // Lowest id wins a tie, so the text does not flicker between players.
                if (score.Kills > leaderKills || (score.Kills == leaderKills && score.Kills > 0 && networkId < leader))
                {
                    leader = networkId;
                    leaderKills = score.Kills;
                }
            }
            LeaderText = leaderKills > 0 ? $"LEAD {PilotName(leader)} {leaderKills}" : "";
            RefreshScoreboard();
        }

        private void RefreshScoreboard()
        {
            _rows.Clear();
            foreach (var (networkId, score) in _scores)
            {
                _chassis.TryGetValue(networkId, out var chassis);
                _rows.Add(new ScoreboardRow(networkId, 0, PlayerName(networkId), chassis ?? "", score.Kills, score.Deaths,
                    networkId == _localNetworkId));
            }
            _rows.Sort(static (a, b) =>
                a.Kills != b.Kills ? b.Kills.CompareTo(a.Kills)
                : a.Deaths != b.Deaths ? a.Deaths.CompareTo(b.Deaths)
                : a.NetworkId.CompareTo(b.NetworkId));

            var place = 0;
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var tied = i > 0 && _rows[i - 1].Kills == row.Kills && _rows[i - 1].Deaths == row.Deaths;
                if (!tied) place = i + 1;
                _rows[i] = new ScoreboardRow(row.NetworkId, place, row.Name, row.Chassis, row.Kills, row.Deaths, row.IsLocal);
            }

            ScoreboardTitle = _rows.Count == 1 ? "FREE FOR ALL // 1 PILOT" : $"FREE FOR ALL // {_rows.Count} PILOTS";
            ScoreboardChanged?.Invoke();
        }

        private void RefreshScoreboardVisible() => ScoreboardVisible = _scoreboardHeld || _phase == MatchPhase.PostGame;

        private void RefreshKillFeed()
        {
            _builder.Clear();
            foreach (var (text, _) in _killFeed)
            {
                if (_builder.Length > 0) _builder.Append('\n');
                _builder.Append(text);
            }
            KillFeedText = _builder.ToString();
        }

        private void RefreshBanner()
        {
            RefreshScoreboardVisible();
            BannerVisible = _phase == MatchPhase.PostGame;
            if (BannerVisible) DeathAlertVisible = false;
            BannerText = !BannerVisible ? ""
                : _winnerNetworkId == 0 ? "DRAW"
                : _winnerNetworkId == _localNetworkId ? "VICTORY"
                : $"{PilotName(_winnerNetworkId)} WINS";
        }

        private void Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(propertyName));
        }
    }
}
