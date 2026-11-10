using Chopper.Presentation.Bridge;
using Chopper.Presentation.Pilots;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Flight;
using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Client only. Drives the HUD's pilot portrait (<see cref="PresentationContext.PilotPortrait"/>) from the local
    /// chopper: its pilot, its pose (the light comes from the scene round it), roll and turn (the head leans and looks),
    /// boost, hull lost (a hit shake), a Blackout jam (glitch), and the pilot's own radio line (the jaw).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LocalChopperHudSystem))]
    public partial class LocalPilotPortraitSystem : PresentationSystemBase
    {
        private Entity _chopper;
        private float _health = -1f;
        private float3 _forward;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var rig = context.PilotPortrait;
            if (rig.Texture == null) return;
            var catalog = context.Config != null ? context.Config.LoadoutCatalog : null;
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (loadout, pose, health, state, entity) in SystemAPI
                         .Query<RefRO<ChopperLoadout>, RefRO<LocalToWorld>, RefRO<Health>, RefRO<ChopperFlightState>>()
                         .WithAll<GhostOwnerIsLocal>().WithEntityAccess())
            {
                var pilot = catalog != null ? catalog.FindPilot(loadout.ValueRO.PilotId) : null;
                rig.SetPilot(pilot);
                rig.UiColour = true;
                if (!rig.HasPilot) return;

                // A new chopper (spawn): no hit and no turn from the old one.
                var forward = pose.ValueRO.Forward;
                var share = health.ValueRO.Max > 0f ? health.ValueRO.Current / health.ValueRO.Max : 1f;
                if (entity != _chopper)
                {
                    _chopper = entity;
                    _health = share;
                    _forward = forward;
                }

                var yawRate = 0f;
                if (deltaTime > 0f)
                {
                    var flat = math.normalizesafe(new float3(forward.x, 0f, forward.z));
                    var before = math.normalizesafe(new float3(_forward.x, 0f, _forward.z));
                    var turn = math.atan2(math.cross(before, flat).y, math.dot(before, flat));
                    yawRate = math.degrees(turn) / deltaTime;
                }
                _forward = forward;
                var hit = math.max(0f, _health - share);
                _health = share;

                var ability = SystemAPI.HasComponent<AbilityState>(entity) ? SystemAPI.GetComponent<AbilityState>(entity) : default;
                var audio = context.Audio;
                var input = new PilotPortraitInput
                {
                    Facing = pose.ValueRO.Rotation,
                    RollDegrees = context.Hud.RollDegrees,
                    YawRate = yawRate,
                    Boosting = state.ValueRO.Boosting,
                    Hit = hit,
                    Voice = rig.OwnVoice(audio),
                    Glitch = ability.Jammed ? 0.6f : 0f,
                };
                rig.Tick(UnityEngine.Time.unscaledDeltaTime, input);
                return;
            }
            _chopper = Entity.Null;
        }
    }
}
