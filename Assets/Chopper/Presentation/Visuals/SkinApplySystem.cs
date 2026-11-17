using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Loadout;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;

namespace Chopper.Presentation.Visuals
{
    /// <summary>The runtime skin shader's skin index on one Entities Graphics renderer (a per-instance override).</summary>
    [MaterialProperty(SkinLibrary.SkinIndexProperty)]
    public struct SkinIndexOverride : IComponentData
    {
        public float Value;
    }

    /// <summary>
    /// Client only. Shows each chopper's <see cref="ChopperSkin"/>: the row of its skin in the game config's
    /// <see cref="SkinLibrary"/> goes to every renderer entity of the chopper as <see cref="SkinIndexOverride"/>. Only the
    /// runtime skin shader reads it (other materials ignore it). Module visuals (GameObjects) get the same row from
    /// <see cref="ModuleVisualSystem"/>.
    /// While a chopper repairs (<see cref="ChopperRepair"/>), a heal sheen fades in and is packed into the same value
    /// (<see cref="SkinLibrary.PackIndex"/>); the shader pulses green over the hull.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SkinApplySystem : PresentationSystemBase
    {
        private readonly Dictionary<Entity, float> _shown = new();
        private readonly Dictionary<Entity, float> _heal = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var library = context.Config != null ? context.Config.Skins : null;
            if (library == null) return;

            var effects = context.Effects != null ? context.Effects.Settings : null;
            var sheen = effects != null ? effects.HealSheen : 0f;
            var fadeStep = effects != null && effects.HealSheenFade > 0f ? SystemAPI.Time.DeltaTime / effects.HealSheenFade : 1f;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            _seen.Clear();
            var repairs = SystemAPI.GetComponentLookup<ChopperRepair>(true);
            var drones = SystemAPI.GetComponentLookup<RepairDroneState>(true);
            foreach (var (skin, group, entity) in SystemAPI.Query<RefRO<ChopperSkin>, DynamicBuffer<LinkedEntityGroup>>()
                         .WithEntityAccess())
            {
                _seen.Add(entity);
                var repairing = sheen > 0f && RepairDrone.IsBeingHealed(entity, repairs, drones) &&
                                !(SystemAPI.HasComponent<Health>(entity) && SystemAPI.GetComponent<Health>(entity).IsDead);
                _heal.TryGetValue(entity, out var heal);
                heal = UnityEngine.Mathf.MoveTowards(heal, repairing ? 1f : 0f, fadeStep);
                _heal[entity] = heal;
                var row = SkinLibrary.PackIndex(library.IndexOf(skin.ValueRO.SkinId), heal * sheen);
                if (_shown.TryGetValue(entity, out var shown) && shown == row) continue;
                _shown[entity] = row;
                foreach (var linked in group)
                {
                    var target = linked.Value;
                    if (!SystemAPI.HasComponent<MaterialMeshInfo>(target)) continue;
                    if (SystemAPI.HasComponent<SkinIndexOverride>(target))
                        ecb.SetComponent(target, new SkinIndexOverride { Value = row });
                    else
                        ecb.AddComponent(target, new SkinIndexOverride { Value = row });
                }
            }
            ecb.Playback(EntityManager);

            _gone.Clear();
            foreach (var entity in _shown.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                _shown.Remove(entity);
                _heal.Remove(entity);
            }
        }
    }
}
