using System.Collections.Generic;
using Chopper.Presentation.Bridge;
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
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SkinApplySystem : PresentationSystemBase
    {
        private readonly Dictionary<Entity, int> _shown = new();
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

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            _seen.Clear();
            foreach (var (skin, group, entity) in SystemAPI.Query<RefRO<ChopperSkin>, DynamicBuffer<LinkedEntityGroup>>()
                         .WithEntityAccess())
            {
                _seen.Add(entity);
                var row = library.IndexOf(skin.ValueRO.SkinId);
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
            foreach (var entity in _gone) _shown.Remove(entity);
        }
    }
}
