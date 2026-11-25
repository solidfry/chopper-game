using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Match;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Capture the Nuke docks: one <see cref="NukeDockRig"/> (robot arms and work drones) at each base dock,
    /// facing the nuke pad like the base model. The arms and drones work while the nuke sits in that dock and arms
    /// (<see cref="NukePhase.Docked"/>, not yet armed); otherwise they rest.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeDockSystem : PresentationSystemBase
    {
        private readonly Dictionary<byte, NukeDockRig> _rigs = new();
        private PresentationContext _context;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy() => Clear();

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_context != context)
            {
                Clear();
                _context = context;
            }
            var settings = context.Effects.Settings;
            if (settings == null || context.Match.Mode != MatchMode.CaptureTheNuke)
            {
                Clear();
                return;
            }

            byte armingAt = TeamRules.None;
            var progress = 0f;
            foreach (var nuke in SystemAPI.Query<RefRO<NukeState>>())
            {
                if (nuke.ValueRO.Phase != NukePhase.Docked || nuke.ValueRO.ArmedBy != TeamRules.None) continue;
                armingAt = nuke.ValueRO.DockTeam;
                progress = nuke.ValueRO.ArmProgress;
            }

            var pad = SystemAPI.TryGetSingleton<NukePad>(out var p) ? p.Position : float3.zero;
            var time = UnityEngine.Time.time;
            var deltaTime = UnityEngine.Time.deltaTime;
            foreach (var zone in SystemAPI.Query<RefRO<NukeBase>>())
            {
                var team = zone.ValueRO.Team;
                if (!_rigs.TryGetValue(team, out var rig))
                {
                    var toPad = pad - zone.ValueRO.Position;
                    toPad.y = 0f;
                    var facing = math.lengthsq(toPad) > 1f ? Quaternion.LookRotation((Vector3)math.normalize(toPad)) : Quaternion.identity;
                    rig = new NukeDockRig(settings, zone.ValueRO.Dock, facing);
                    _rigs[team] = rig;
                }
                rig.Tick(armingAt == team, progress, time, deltaTime);
            }
        }

        private void Clear()
        {
            foreach (var rig in _rigs.Values) rig.Dispose();
            _rigs.Clear();
        }
    }
}
