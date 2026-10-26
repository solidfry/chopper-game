using System;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;

namespace Chopper.Presentation.Feedback
{
    /// <summary>Fills the <see cref="Hud.MatchHudModel"/> from match messages and plays the match end sound.</summary>
    public sealed class MatchFeedback : IDisposable
    {
        private readonly PresentationContext _context;
        private readonly IDisposable[] _subscriptions;

        public MatchFeedback(PresentationContext context)
        {
            _context = context;
            var events = context.Events;
            _subscriptions = new[]
            {
                events.Subscribe<LocalPlayerIdentified>(m => context.Match.SetLocalPlayer(m.NetworkId)),
                events.Subscribe<ScoreChanged>(m => context.Match.ApplyScore(m.NetworkId, m.Kills, m.Deaths)),
                events.Subscribe<PlayerLeft>(m => context.Match.RemovePlayer(m.NetworkId)),
                events.Subscribe<PlayerKilled>(m => context.Match.AddKill(m.KillerNetworkId, m.VictimNetworkId)),
                events.Subscribe<MatchStateChanged>(OnMatchState),
            };
        }

        public void Dispose()
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }

        private void OnMatchState(MatchStateChanged state)
        {
            var wasOver = _context.Match.BannerVisible;
            _context.Match.SetMatch(state.Phase, state.TimeRemaining, state.WinnerNetworkId);

            var sounds = _context.Sounds;
            if (!wasOver && _context.Match.BannerVisible && sounds != null)
                _context.Audio.Play(sounds.MatchEnd);
        }
    }
}
