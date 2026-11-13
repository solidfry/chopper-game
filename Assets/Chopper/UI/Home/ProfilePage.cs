using System;
using System.Collections.Generic;
using System.Globalization;
using Chopper.Presentation.Profile;
using Chopper.Presentation.Results;
using Chopper.UI.Cockpit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Chopper.UI.Home
{
    /// <summary>
    /// The home screen's profile page (<c>page-profile</c> in Home.uxml, styles in Profile.uss): who you are (the
    /// account avatar, or your pilot's live portrait without one), level and XP, the kill / death / ratio cards, the
    /// service record, recent matches, the award case (every award, won or locked) and achievements with progress.
    /// Reads <see cref="ProfileService"/> and <see cref="IPlayerIdentity"/>; <see cref="HomeView"/> owns it and shows
    /// the pilot's face (it owns the live portrait).
    /// </summary>
    internal sealed class ProfilePage
    {
        private readonly VisualElement _root;
        private readonly Label _source;
        private readonly VisualElement _avatar;
        private readonly Label _name;
        private readonly Label _pilot;
        private readonly Label _level;
        private readonly SegmentBar _xp;
        private readonly Label _xpText;
        private readonly Label _kills;
        private readonly Label _deaths;
        private readonly Label _ratio;
        private readonly Label _matches;
        private readonly Label _wins;
        private readonly Label _winRate;
        private readonly VisualElement _record;
        private readonly VisualElement _recent;
        private readonly VisualElement _awards;
        private readonly Label _awardsTitle;
        private readonly VisualElement _achievements;
        private readonly Label _achievementsTitle;
        /// <summary>Chassis and pilot mastery rows (null with an old layout).</summary>
        private readonly VisualElement _mastery;

        public ProfilePage(VisualElement root)
        {
            _root = root;
            _source = root.Q<Label>("pf-source");
            _avatar = root.Q("pf-avatar");
            Scan = root.Q<CodecScan>("pf-scan");
            _name = root.Q<Label>("pf-name");
            _pilot = root.Q<Label>("pf-pilot");
            _level = root.Q<Label>("pf-level");
            _xp = root.Q<SegmentBar>("pf-xp");
            _xpText = root.Q<Label>("pf-xp-text");
            _kills = root.Q<Label>("pf-kills");
            _deaths = root.Q<Label>("pf-deaths");
            _ratio = root.Q<Label>("pf-ratio");
            _matches = root.Q<Label>("pf-matches");
            _wins = root.Q<Label>("pf-wins");
            _winRate = root.Q<Label>("pf-winrate");
            _record = root.Q("pf-record");
            _recent = root.Q("pf-recent");
            _awards = root.Q("pf-awards");
            _awardsTitle = root.Q<Label>("pf-awards-title");
            _achievements = root.Q("pf-achievements");
            _achievementsTitle = root.Q<Label>("pf-achievements-title");
            _mastery = root.Q("pf-mastery");
            Back = root.Q<Button>("profile-back");
        }

        public Button Back { get; }
        /// <summary>
        /// The avatar frame: it holds the picture itself (as the codex frame does), the account avatar or, without one,
        /// the pilot's live face that <see cref="HomeView"/> puts there.
        /// </summary>
        public VisualElement AvatarPicture => _avatar;
        public CodecScan Scan { get; }

        /// <summary>Fills the page. True when the account has its own avatar (else the caller shows the pilot).</summary>
        public bool Show(ProfileService service, IPlayerIdentity identity, string pilotCallSign, Color pilotAccent,
            IReadOnlyList<TrackProgress> mastery = null)
        {
            var p = service != null ? service.Profile : new PlayerProfile();
            var hasAvatar = identity != null && identity.Avatar != null;
            _source.text = identity != null ? $"ID // {identity.Source}" : "ID // LOCAL";
            _name.text = identity != null ? identity.DisplayName : "OPERATOR";
            _pilot.text = string.IsNullOrEmpty(pilotCallSign) ? "NO PILOT" : $"FLYING {pilotCallSign}";
            _pilot.style.color = pilotAccent;
            // A Steam avatar is square; the pilot portrait is 2:3.
            _avatar.EnableInClassList("pf-id__avatar--square", hasAvatar);
            if (hasAvatar)
            {
                AvatarPicture.style.backgroundImage = new StyleBackground(identity.Avatar);
                AvatarPicture.style.unityBackgroundImageTintColor = Color.white;
            }

            if (service != null)
            {
                var account = service.Account;
                _level.text = $"LVL {account.Level:00}";
                _xp.value = account.Fill;
                _xpText.text = $"{account.NeedText}    TOTAL {p.totalXp:N0}";
            }
            else
            {
                var level = PilotLevels.Level(p.totalXp, out var into, out var needed);
                _level.text = $"LVL {level:00}";
                _xp.value = needed > 0 ? (float)into / needed : 0f;
                _xpText.text = $"{into:N0} / {needed:N0} XP    TOTAL {p.totalXp:N0}";
            }
            FillMastery(mastery);

            _kills.text = p.kills.ToString(CultureInfo.InvariantCulture);
            _deaths.text = p.deaths.ToString(CultureInfo.InvariantCulture);
            _ratio.text = p.KillDeathRatio.ToString("0.00", CultureInfo.InvariantCulture);
            _matches.text = p.matches.ToString(CultureInfo.InvariantCulture);
            _wins.text = p.wins.ToString(CultureInfo.InvariantCulture);
            _winRate.text = p.matches > 0 ? $"{p.WinRate:0%}" : "-";

            FillRecord(p);
            FillRecent(p);
            FillAwards(p);
            FillAchievements(p);
            return hasAvatar;
        }

        private void FillRecord(PlayerProfile p)
        {
            _record.Clear();
            Row("ACCURACY", p.shots > 0 ? $"{p.Accuracy:0.0%}" : "-");
            Row("SHOTS / HITS", $"{p.shots:N0} / {p.hits:N0}");
            Row("DAMAGE DEALT", $"{p.damageDealt:N0}");
            Row("DAMAGE TAKEN", $"{p.damageTaken:N0}");
            Row("BEST STREAK", p.bestStreak.ToString(CultureInfo.InvariantCulture));
            Row("LONGEST KILL", p.longestKill > 0f ? $"{p.longestKill:N0} M" : "-");
            Row("FIRST BLOODS", p.firstBloods.ToString(CultureInfo.InvariantCulture));
            Row("MISSILE HITS", p.missileHits.ToString(CultureInfo.InvariantCulture));
            Row("MISSILES DODGED", p.missilesDodged.ToString(CultureInfo.InvariantCulture));
            Row("FLARES", p.flares.ToString(CultureInfo.InvariantCulture));
            Row("HULL MENDED", $"{p.repaired:N0}");
            Row("ABILITIES USED", p.abilities.ToString(CultureInfo.InvariantCulture));
            Row("RAMS", p.rams.ToString(CultureInfo.InvariantCulture));
            Row("CRASHES", p.crashes.ToString(CultureInfo.InvariantCulture));
            Row("LEFT THE ARENA", p.boundsDeaths.ToString(CultureInfo.InvariantCulture));
        }

        private void Row(string label, string value)
        {
            var row = Element("pf-row");
            row.Add(Text(label, "pf-row__label", "chopper-title"));
            row.Add(Text(value, "pf-row__value", "chopper-title"));
            _record.Add(row);
        }

        private void FillRecent(PlayerProfile p)
        {
            _recent.Clear();
            if (p.recent.Count == 0)
            {
                _recent.Add(Text("No matches yet. Fly one.", "pf-empty", "chopper-body"));
                return;
            }
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var match in p.recent)
            {
                var row = Element("pf-match");
                row.EnableInClassList("pf-match--won", match.won);
                row.EnableInClassList("pf-match--lost", !match.won && !match.draw);
                row.Add(Text(match.won ? "WIN" : match.draw ? "DRAW" : "LOSS", "pf-match__result", "chopper-title"));
                row.Add(Text($"{Ordinal(match.place)}/{match.players}", "pf-match__place", "chopper-title"));
                row.Add(Text($"{match.kills} K / {match.deaths} D  {match.pilot}", "pf-match__kd", "chopper-body"));
                row.Add(Text($"+{match.xp} XP", "pf-match__xp", "chopper-title"));
                row.Add(Text(Ago(now - match.unixTime), "pf-match__when", "chopper-body"));
                _recent.Add(row);
            }
        }

        private void FillAwards(PlayerProfile p)
        {
            _awards.Clear();
            var total = 0;
            var won = 0;
            foreach (var (title, tone, description) in MatchAwards.Catalogue())
            {
                total++;
                var count = p.AwardCountOf(title);
                if (count > 0) won++;
                var card = Element("pf-award");
                card.EnableInClassList("pf-award--funny", tone == AwardTone.Funny);
                card.EnableInClassList("pf-award--shame", tone == AwardTone.Shame);
                card.EnableInClassList("pf-award--locked", count == 0);
                var head = Element("pf-award__head");
                head.Add(Text(title, "pf-award__title", "chopper-title"));
                head.Add(Text(count > 0 ? $"x{count}" : "", "pf-award__count", "chopper-title"));
                card.Add(head);
                card.Add(Text(description, "pf-award__line", "chopper-body"));
                _awards.Add(card);
            }
            _awardsTitle.text = $"// AWARD CASE   {won} / {total}";
        }

        /// <summary>One row per track (chassis classes, then pilots), in the achievement row style.</summary>
        private void FillMastery(IReadOnlyList<TrackProgress> tracks)
        {
            if (_mastery == null) return;
            _mastery.Clear();
            if (tracks == null) return;
            foreach (var track in tracks)
            {
                var row = Element("pf-ach");
                row.EnableInClassList("pf-ach--done", track.IsMax);
                var text = Element("pf-ach__text");
                text.Add(Text(track.Label, "pf-ach__title", "chopper-title"));
                text.Add(Text($"{track.LevelText}    TOTAL {track.TotalXp:N0} XP", "pf-ach__line", "chopper-body"));
                row.Add(text);
                var progress = Element("pf-ach__progress");
                var bar = new SegmentBar { segments = 10, gap = 2f, value = track.Fill };
                bar.AddToClassList("pf-ach__bar");
                progress.Add(bar);
                progress.Add(Text(track.NeedText, "pf-ach__amount", "chopper-title"));
                row.Add(progress);
                _mastery.Add(row);
            }
        }

        private void FillAchievements(PlayerProfile p)
        {
            _achievements.Clear();
            var done = 0;
            foreach (var achievement in Achievements.All)
            {
                var unlocked = p.HasAchievement(achievement.Id);
                if (unlocked) done++;
                var row = Element("pf-ach");
                row.EnableInClassList("pf-ach--done", unlocked);
                var text = Element("pf-ach__text");
                text.Add(Text(achievement.Title, "pf-ach__title", "chopper-title"));
                text.Add(Text(achievement.Description, "pf-ach__line", "chopper-body"));
                row.Add(text);
                var progress = Element("pf-ach__progress");
                var bar = new SegmentBar { segments = 10, gap = 2f, value = unlocked ? 1f : achievement.Progress(p) };
                bar.AddToClassList("pf-ach__bar");
                progress.Add(bar);
                var value = Math.Min(achievement.Value(p), achievement.Target);
                progress.Add(Text(unlocked ? "DONE" : $"{value:N0} / {achievement.Target:N0}", "pf-ach__amount", "chopper-title"));
                row.Add(progress);
                _achievements.Add(row);
            }
            _achievementsTitle.text = $"// ACHIEVEMENTS   {done} / {Achievements.All.Count}";
        }

        private static string Ago(long seconds)
        {
            if (seconds < 60) return "NOW";
            if (seconds < 3600) return $"{seconds / 60}M AGO";
            if (seconds < 86400) return $"{seconds / 3600}H AGO";
            return $"{seconds / 86400}D AGO";
        }

        private static string Ordinal(int place) => place switch
        {
            1 => "1ST",
            2 => "2ND",
            3 => "3RD",
            _ => $"{place}TH",
        };

        private static VisualElement Element(string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }

        private static Label Text(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in classes) label.AddToClassList(c);
            return label;
        }
    }
}
