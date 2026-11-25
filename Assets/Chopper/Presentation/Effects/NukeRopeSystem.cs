using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Capture the Nuke cable (CTN 4b-2d): a line from the carrier's winch to the hook eye of the nuke it
    /// carries (the server holds the nuke <see cref="NukeZones.HangBelow"/> under it). While a chopper hovers over a free
    /// nuke to hook it, its line runs down toward the nuke as the hook bar fills, and reaches it at the hook.
    /// The carrier is the chopper whose replicated <see cref="NukeCarry.Carrying"/> is set; the hooking chopper is the
    /// nearest live one of <see cref="NukeState.HookTeam"/> in its own reach (the same pick as the server).
    /// Look and winch point: <see cref="CombatEffectsSettings"/>, "Capture the Nuke rope".
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeRopeSystem : PresentationSystemBase
    {
        /// <summary>Without a carry flag yet (a snapshot behind the phase): a carrier-team chopper this close to where
        /// the cable starts counts.</summary>
        private const float CarrierSearch = 12f;

        private readonly Dictionary<Entity, NukeRopeView> _ropes = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private PresentationContext _context;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            foreach (var rope in _ropes.Values) rope.Dispose();
            _ropes.Clear();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var settings = context.Effects.Settings;
            _seen.Clear();
            if (settings != null && settings.NukeRopeMaterial != null && context.Match.Mode == MatchMode.CaptureTheNuke)
            {
                var deltaTime = UnityEngine.Time.deltaTime;
                foreach (var (nuke, pose, entity) in SystemAPI.Query<RefRO<NukeState>, RefRO<LocalToWorld>>().WithEntityAccess())
                {
                    var s = nuke.ValueRO;
                    var end = pose.ValueRO.Position;
                    Entity chopper;
                    float reach;
                    if (s.Phase == NukePhase.Carried)
                    {
                        chopper = Carrier(s.CarrierTeam, end);
                        reach = 1f;
                    }
                    else if (s.Phase != NukePhase.Detonated && s.HookTeam != TeamRules.None &&
                             (s.Phase == NukePhase.Planted ? s.DefuseProgress : s.HookProgress) > 0f)
                    {
                        // Hooking from the pad, a drop or a dock; over a planted nuke the same line disarms it.
                        chopper = Hooker(s.HookTeam, end);
                        reach = s.Phase == NukePhase.Planted ? s.DefuseProgress : s.HookProgress;
                    }
                    else continue;
                    if (chopper == Entity.Null) continue;

                    _seen.Add(entity);
                    if (!_ropes.TryGetValue(entity, out var rope))
                    {
                        rope = new NukeRopeView(settings);
                        _ropes[entity] = rope;
                    }
                    var winch = math.transform(SystemAPI.GetComponent<LocalToWorld>(chopper).Value, settings.NukeRopeWinch);
                    rope.Draw(settings, winch, end, reach, deltaTime);
                }
            }

            foreach (var pair in _ropes)
                if (!_seen.Contains(pair.Key)) pair.Value.Hide();
            // Nukes gone from the world (new round's ghost, match end): drop their cables.
            _gone.Clear();
            foreach (var key in _ropes.Keys)
                if (!SystemAPI.Exists(key)) _gone.Add(key);
            foreach (var key in _gone)
            {
                _ropes[key].Dispose();
                _ropes.Remove(key);
            }
        }

        /// <summary>The chopper flagged as carrying; else the carrier team's chopper nearest above the nuke.</summary>
        private Entity Carrier(byte team, float3 nuke)
        {
            foreach (var (carry, chopper) in SystemAPI.Query<RefRO<NukeCarry>>().WithEntityAccess())
                if (carry.ValueRO.Carrying) return chopper;

            var above = nuke + new float3(0f, NukeZones.HangBelow, 0f);
            var best = Entity.Null;
            var bestDistance = CarrierSearch;
            foreach (var (pose, owner, health, chopper) in
                     SystemAPI.Query<RefRO<LocalToWorld>, RefRO<Team>, RefRO<Health>>().WithAll<NukeCarry>().WithEntityAccess())
            {
                if (owner.ValueRO.Value != team || health.ValueRO.IsDead) continue;
                var d = math.distance(pose.ValueRO.Position, above);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = chopper;
            }
            return best;
        }

        /// <summary>The nearest live chopper of <paramref name="team"/> in its own hook reach of the nuke.</summary>
        private Entity Hooker(byte team, float3 nuke)
        {
            var best = Entity.Null;
            var bestDistance = float.MaxValue;
            foreach (var (pose, owner, health, chopper) in
                     SystemAPI.Query<RefRO<LocalToWorld>, RefRO<Team>, RefRO<Health>>().WithAll<NukeCarry>().WithEntityAccess())
            {
                if (owner.ValueRO.Value != team || health.ValueRO.IsDead) continue;
                var range = SystemAPI.HasComponent<NukeHookConfig>(chopper) ? SystemAPI.GetComponent<NukeHookConfig>(chopper).Range : NukeHookConfig.BaseRange;
                var d = math.distance(pose.ValueRO.Position, nuke);
                if (d > range || d >= bestDistance) continue;
                bestDistance = d;
                best = chopper;
            }
            return best;
        }

        /// <summary>New context (scene change): the old cables went with the old scene.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            foreach (var rope in _ropes.Values) rope.Dispose();
            _ropes.Clear();
            _context = context;
        }
    }
}
