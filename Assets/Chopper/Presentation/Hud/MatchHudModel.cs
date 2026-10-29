using System;
using System.Collections.Generic;
using System.Text;
using Chopper.Simulation.Match;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// View model for the match part of the HUD: timer, scores, kill feed and the end-of-match banner.
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
        private MatchPhase _phase;
        private float _timeRemaining;
        private int _winnerNetworkId;

        private string _timerText = "00:00";
        private string _localScoreText = "0 K / 0 D";
        private string _leaderText = "";
        private string _killFeedText = "";
        private string _bannerText = "";
        private bool _bannerVisible;

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

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

        public void SetLocalPlayer(int networkId)
        {
            if (_localNetworkId == networkId) return;
            _localNetworkId = networkId;
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
            if (_scores.Remove(networkId))
                RefreshScores();
        }

        /// <param name="killerNetworkId">0 when nobody gets the kill (crash, bounds).</param>
        public void AddKill(int killerNetworkId, int victimNetworkId)
        {
            var victim = PilotName(victimNetworkId);
            var text = killerNetworkId == 0 || killerNetworkId == victimNetworkId
                ? $"{victim} CRASHED"
                : $"{PilotName(killerNetworkId)} > {victim}";

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

        private string PilotName(int networkId) => networkId == _localNetworkId ? "YOU" : $"PILOT {networkId}";

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
        }

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
            BannerVisible = _phase == MatchPhase.PostGame;
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
