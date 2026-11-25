using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Client only. Capture the Nuke centre line under the reticle (<see cref="NukeCallout"/>): what the local player
    /// should do now (hook, fly home, arm, escort, defend, defuse) with a progress bar, and a short flash when the nuke
    /// changes (<see cref="NukeChanged"/>). Hidden while the local player has no chopper (the death alert is there).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeCalloutSystem : PresentationSystemBase
    {
        private readonly List<NukeChanged> _changes = new();
        private PresentationContext _context;
        private IDisposable _subscription;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => _subscription?.Dispose();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var match = context.Match;
            if (match.Mode != MatchMode.CaptureTheNuke)
            {
                _changes.Clear();
                match.SetObjective(Callout.Empty);
                match.SetNukeTimer(NukeTimer.None);
                return;
            }

            byte self = TeamRules.None;
            var position = float3.zero;
            var carrying = false;
            var range = NukeHookConfig.Base.Range;
            foreach (var (pose, carry, chopper) in SystemAPI.Query<RefRO<LocalToWorld>, RefRO<NukeCarry>>().WithAll<GhostOwnerIsLocal>().WithEntityAccess())
            {
                self = SystemAPI.HasComponent<Team>(chopper) ? SystemAPI.GetComponent<Team>(chopper).Value : TeamRules.None;
                if (SystemAPI.HasComponent<NukeHookConfig>(chopper)) range = SystemAPI.GetComponent<NukeHookConfig>(chopper).Range;
                position = pose.ValueRO.Position;
                carrying = carry.ValueRO.Carrying;
            }

            foreach (var change in _changes) match.FlashObjective(NukeCallout.Flash(change.Change, change.Team, self));
            _changes.Clear();

            var status = Callout.Empty;
            var timer = NukeTimer.None;
            foreach (var (nuke, pose) in SystemAPI.Query<RefRO<NukeState>, RefRO<LocalToWorld>>())
            {
                var at = pose.ValueRO.Position;
                var inRange = NukeRules.CanHook(nuke.ValueRO, self) && math.distance(position, at) <= range;
                status = NukeCallout.Status(nuke.ValueRO, self, carrying, inRange, NukeTuning.Default.DropResetSeconds);
                timer = NukeCallout.Timer(nuke.ValueRO, self, NukeTuning.Default);
                break;
            }
            match.SetObjective(status);
            match.SetNukeTimer(timer);
        }

        /// <summary>New context (scene change): listen on the new scene's events.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _changes.Clear();
            _context = context;
            _subscription = context.Events.Subscribe<NukeChanged>(_changes.Add);
        }
    }
}
