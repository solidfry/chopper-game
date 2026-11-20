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
        /// <summary>Team modes: the player's team (0 in Free For All).</summary>
        public readonly byte Team;

        public ScoreboardRow(int networkId, int place, string name, string chassis, int kills, int deaths, bool isLocal, byte team = 0)
        {
            NetworkId = networkId;
            Place = place;
            Name = name;
            Chassis = chassis;
            Kills = kills;
            Deaths = deaths;
            IsLocal = isLocal;
            Team = team;
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
        private MatchMode _mode;
        private int _teamCount;
        private byte _winnerTeam;
        private readonly Dictionary<int, byte> _teams = new();
        private readonly int[] _teamScores = new int[TeamRules.MaxTeams + 1];

        /// <summary>Team names in order (team 1 = ALPHA). Shown on the HUD, the scoreboard and the banner.</summary>
        public static readonly string[] TeamNames = { "", "ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT", "GOLF", "HOTEL" };

        public static string TeamName(byte team) => team < TeamNames.Length ? TeamNames[team] : $"TEAM {team}";

        public bool IsTeamMode => TeamRules.IsTeamMode(_mode);
        public MatchMode Mode => _mode;
        public byte LocalTeam => _teams.TryGetValue(_localNetworkId, out var team) ? team : TeamRules.None;
        public int TeamScore(byte team) => team < _teamScores.Length ? _teamScores[team] : 0;

        private string _timerText = "00:00";
        private string _localScoreText = "0 K / 0 D";
        private string _leaderText = "";
        private string _killFeedText = "";
        private string _bannerText = "";
        private bool _bannerVisible;
        private string _deathAlertText = "";
        private bool _deathAlertVisible;
        private string _respawnText = "";
        /// <summary>Seconds until the next chopper; below 0 = no countdown known.</summary>
        private float _respawnLeft = -1f;

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

        private string _localKillsText = "0";
        private string _localDeathsText = "0";

        /// <summary>Own kills as a number (the top-left kill / death card).</summary>
        [CreateProperty]
        public string LocalKillsText
        {
            get => _localKillsText;
            private set => Set(ref _localKillsText, value, nameof(LocalKillsText));
        }

        /// <summary>Own deaths as a number (the top-left kill / death card).</summary>
        [CreateProperty]
        public string LocalDeathsText
        {
            get => _localDeathsText;
            private set => Set(ref _localDeathsText, value, nameof(LocalDeathsText));
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

        /// <summary>"REDEPLOY IN 4" under the death message, then "REDEPLOYING" (the kill cam can hold a little
        /// longer). Empty when no countdown is known.</summary>
        [CreateProperty]
        public string RespawnText
        {
            get => _respawnText;
            private set => Set(ref _respawnText, value, nameof(RespawnText));
        }

        /// <summary>You have a chopper again.</summary>
        public void ClearDeathAlert()
        {
            DeathAlertVisible = false;
            _respawnLeft = -1f;
            RespawnText = "";
        }

        /// <summary>The server's respawn delay for your death; <see cref="Tick"/> counts it down.</summary>
        public void SetRespawnCountdown(float seconds)
        {
            _respawnLeft = MathF.Max(0f, seconds);
            RefreshRespawn();
        }

        private void RefreshRespawn()
        {
            if (_respawnLeft < 0f) RespawnText = "";
            else if (_respawnLeft <= 0f) RespawnText = "REDEPLOYING";
            else RespawnText = $"REDEPLOY IN {(int)MathF.Ceiling(_respawnLeft)}";
        }

        // ---- XP popup (Battlefield style): a title, the victim, a stack of "+XP" lines and a total that counts up. ----

        /// <summary>Seconds a popup with a kill stays after its last award; a new award in that time adds to it (a combo).</summary>
        public const float XpPopupSeconds = 3.2f;
        /// <summary>Seconds for small awards only (repair, assist, evaded), so a stream of them does not hold the popup up.</summary>
        public const float XpSmallPopupSeconds = 1.8f;
        /// <summary>Lines kept in one popup, newest at the bottom. The same award again joins its line ("EVADED x3  +75").</summary>
        public const int MaxXpLines = 3;
        /// <summary>How fast the shown total counts up to the real one (its share per second, so big jumps roll fast).</summary>
        private const float XpCountRate = 9f;
        private const char Newline = '\n';

        private readonly List<string> _xpLines = new();
        private string _xpLastLabel = "";
        private int _xpLastCount;
        private int _xpLastSum;
        private float _xpRemaining;
        private int _xpTarget;
        private float _xpShown;
        private int _xpTitleRank = -1;
        private string _xpTitle = "";
        private string _xpName = "";
        private string _xpLinesText = "";
        private string _xpTotalText = "";
        private bool _xpVisible;
        private int _xpPulse;
        private bool _xpBig;

        [CreateProperty] public string XpTitle { get => _xpTitle; private set => Set(ref _xpTitle, value, nameof(XpTitle)); }
        /// <summary>The victim's name (kills and assists), else "".</summary>
        [CreateProperty] public string XpName { get => _xpName; private set => Set(ref _xpName, value, nameof(XpName)); }
        /// <summary>"KILL  +100" lines, one per award part.</summary>
        [CreateProperty] public string XpLinesText { get => _xpLinesText; private set => Set(ref _xpLinesText, value, nameof(XpLinesText)); }
        /// <summary>"+124", counting up.</summary>
        [CreateProperty] public string XpTotalText { get => _xpTotalText; private set => Set(ref _xpTotalText, value, nameof(XpTotalText)); }
        [CreateProperty] public bool XpVisible { get => _xpVisible; private set => Set(ref _xpVisible, value, nameof(XpVisible)); }
        /// <summary>Goes up at each award: the view plays the punch animation again.</summary>
        [CreateProperty] public int XpPulse { get => _xpPulse; private set => Set(ref _xpPulse, value, nameof(XpPulse)); }
        /// <summary>The popup holds a kill: the big, hot style.</summary>
        [CreateProperty] public bool XpBig { get => _xpBig; private set => Set(ref _xpBig, value, nameof(XpBig)); }

        /// <summary>Adds an award to the popup (or starts one). <paramref name="otherNetworkId"/>: the victim, 0 = none.</summary>
        public void AddXp(XpReason reason, int amount, int otherNetworkId, int damageXp)
        {
            if (amount <= 0 && damageXp <= 0) return;
            if (!_xpVisible) ClearXp();

            var (title, line, rank) = reason switch
            {
                XpReason.Kill => ("ENEMY DOWN", "KILL", 4),
                XpReason.BotKill => ("BOT DOWN", "BOT KILL", 3),
                XpReason.Assist => ("ASSIST", "ASSIST", 2),
                XpReason.MissileEvaded => ("EVADED", "EVADED", 1),
                _ => ("REPAIRED", "REPAIR", 0),
            };
            // The best event of the combo keeps the title (a kill outranks the repair after it).
            if (rank >= _xpTitleRank)
            {
                _xpTitleRank = rank;
                XpTitle = title;
                XpName = otherNetworkId != 0 && reason is XpReason.Kill or XpReason.BotKill or XpReason.Assist ? PlayerName(otherNetworkId) : "";
            }
            if (amount > 0) AddXpLine(line, amount);
            if (damageXp > 0) AddXpLine("DAMAGE", damageXp);
            _xpTarget += amount + damageXp;
            _xpRemaining = _xpTitleRank >= 3 ? XpPopupSeconds : XpSmallPopupSeconds;
            XpBig = _xpTitleRank >= 3;
            XpVisible = true;
            XpPulse = _xpPulse + 1;
        }

        private void AddXpLine(string label, int amount)
        {
            if (_xpLines.Count > 0 && label == _xpLastLabel)
            {
                _xpLastCount++;
                _xpLastSum += amount;
                _xpLines[^1] = $"{label} x{_xpLastCount}  +{_xpLastSum}";
                XpLinesText = string.Join(Newline, _xpLines);
                return;
            }
            _xpLastLabel = label;
            _xpLastCount = 1;
            _xpLastSum = amount;
            _xpLines.Add($"{label}  +{amount}");
            if (_xpLines.Count > MaxXpLines) _xpLines.RemoveAt(0);
            XpLinesText = string.Join(Newline, _xpLines);
        }

        private void ClearXp()
        {
            _xpLines.Clear();
            _xpLastLabel = "";
            _xpTarget = 0;
            _xpShown = 0f;
            _xpTitleRank = -1;
            XpLinesText = "";
            XpTotalText = "";
        }

        private void TickXp(float deltaTime)
        {
            if (!_xpVisible) return;
            if (_xpShown < _xpTarget)
            {
                var step = MathF.Max((_xpTarget - _xpShown) * XpCountRate * deltaTime, 60f * deltaTime);
                _xpShown = MathF.Min(_xpTarget, _xpShown + step);
                XpTotalText = $"+{(int)_xpShown}";
            }
            _xpRemaining -= deltaTime;
            if (_xpRemaining <= 0f) XpVisible = false;
        }

        public void SetLocalPlayer(int networkId)
        {
            if (_localNetworkId == networkId) return;
            _localNetworkId = networkId;
            LocalPilotText = _callSigns.TryGetValue(networkId, out var sign) ? sign : "";
            RefreshScores();
            RefreshBanner();
        }

        public void ApplyScore(int networkId, int kills, int deaths, byte team = 0)
        {
            _scores[networkId] = (kills, deaths);
            _teams[networkId] = team;
            RefreshScores();
            RefreshBanner();
        }

        /// <summary>Team modes: one team's kills (from the server).</summary>
        public void SetTeamScore(byte team, int score)
        {
            if (team == TeamRules.None || team >= _teamScores.Length) return;
            _teamScores[team] = score;
            RefreshScores();
        }

        public void RemovePlayer(int networkId)
        {
            // Call signs and chassis stay: PilotNamesSystem sends only changes, and the rows come from the scores.
            _teams.Remove(networkId);
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

        public void SetMatch(MatchPhase phase, float timeRemaining, int winnerNetworkId, MatchMode mode = MatchMode.FreeForAll,
            int teamCount = 0, byte winnerTeam = 0)
        {
            _phase = phase;
            _timeRemaining = timeRemaining;
            _winnerNetworkId = winnerNetworkId;
            _mode = mode;
            _teamCount = Math.Min(teamCount, TeamRules.MaxTeams);
            _winnerTeam = winnerTeam;
            RefreshScores();
            RefreshTimer();
            RefreshBanner();
        }

        /// <summary>Counts down the timer and the kill feed. Call once per frame.</summary>
        public void Tick(float deltaTime)
        {
            _timeRemaining = MathF.Max(0f, _timeRemaining - deltaTime);
            RefreshTimer();
            TickXp(deltaTime);
            if (_respawnLeft > 0f)
            {
                _respawnLeft = MathF.Max(0f, _respawnLeft - deltaTime);
                RefreshRespawn();
            }

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
            LocalKillsText = local.Kills.ToString();
            LocalDeathsText = local.Deaths.ToString();

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
            LeaderText = IsTeamMode ? TeamScoresText() : leaderKills > 0 ? $"LEAD {PilotName(leader)} {leaderKills}" : "";
            RefreshScoreboard();
        }

        /// <summary>"ALPHA 12  BRAVO 9" (your team first).</summary>
        private string TeamScoresText()
        {
            _builder.Clear();
            var own = LocalTeam;
            if (own != TeamRules.None && own <= _teamCount) _builder.Append(TeamName(own)).Append(' ').Append(_teamScores[own]);
            for (byte team = 1; team <= _teamCount; team++)
            {
                if (team == own) continue;
                if (_builder.Length > 0) _builder.Append("  ");
                _builder.Append(TeamName(team)).Append(' ').Append(_teamScores[team]);
            }
            return _builder.ToString();
        }

        private void RefreshScoreboard()
        {
            _rows.Clear();
            foreach (var (networkId, score) in _scores)
            {
                _chassis.TryGetValue(networkId, out var chassis);
                _teams.TryGetValue(networkId, out var team);
                _rows.Add(new ScoreboardRow(networkId, 0, PlayerName(networkId), chassis ?? "", score.Kills, score.Deaths,
                    networkId == _localNetworkId, team));
            }
            // Team modes: grouped by team (team 1 first), then by kills.
            _rows.Sort(static (a, b) =>
                a.Team != b.Team ? a.Team.CompareTo(b.Team)
                : a.Kills != b.Kills ? b.Kills.CompareTo(a.Kills)
                : a.Deaths != b.Deaths ? a.Deaths.CompareTo(b.Deaths)
                : a.NetworkId.CompareTo(b.NetworkId));

            var place = 0;
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var newTeam = i == 0 || _rows[i - 1].Team != row.Team;
                if (newTeam) place = 0;
                var tied = !newTeam && _rows[i - 1].Kills == row.Kills && _rows[i - 1].Deaths == row.Deaths;
                if (!tied) place = newTeam ? 1 : i + 1 - FirstOfTeam(i);
                _rows[i] = new ScoreboardRow(row.NetworkId, place, row.Name, row.Chassis, row.Kills, row.Deaths, row.IsLocal, row.Team);
            }

            var mode = _mode == MatchMode.CaptureTheNuke ? "CAPTURE THE NUKE" : IsTeamMode ? "TEAM DEATHMATCH" : "FREE FOR ALL";
            ScoreboardTitle = _rows.Count == 1 ? $"{mode} // 1 PILOT" : $"{mode} // {_rows.Count} PILOTS";
            ScoreboardChanged?.Invoke();
        }

        /// <summary>Index of the first row of row <paramref name="index"/>'s team (rows are sorted by team).</summary>
        private int FirstOfTeam(int index)
        {
            var first = index;
            while (first > 0 && _rows[first - 1].Team == _rows[index].Team) first--;
            return first;
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
                : IsTeamMode ? (_winnerTeam == TeamRules.None ? "DRAW"
                    : _winnerTeam == LocalTeam ? "VICTORY"
                    : $"{TeamName(_winnerTeam)} WINS")
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
