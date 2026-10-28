using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Effects;
using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Client only. Fits every chopper's modules onto its chassis sockets ("lego" build): for each slot of its
    /// <see cref="ChopperLoadout"/>, the module's visual prefab (pooled GameObject) follows the socket each frame.
    /// The loadout comes over the network as module ids; the art is local, so it costs no bandwidth.
    /// Module prefabs can carry their own effects (rotor spin scripts, lights, particles). Their renderers get the
    /// chopper's skin row (<see cref="ChopperSkin"/>, <see cref="SkinLibrary"/>) for the runtime skin shader.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ModuleVisualSystem : PresentationSystemBase
    {
        private sealed class Fitted
        {
            public LoadoutCode Code;
            public int SkinRow;
            public readonly List<(GameObject Visual, float3 Position, quaternion Rotation)> Parts = new();
        }

        private readonly Dictionary<Entity, Fitted> _fitted = new();
        private readonly HashSet<Entity> _seen = new();
        private readonly List<Entity> _gone = new();
        private readonly Dictionary<ushort, ChassisDefinition> _chassis = new();
        private readonly Dictionary<ushort, ModuleDefinition> _modules = new();
        private LoadoutCatalog _indexed;
        /// <summary>The context whose pool holds the current visuals (a new context means a new pool).</summary>
        private PresentationContext _context;
        private MaterialPropertyBlock _block;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_context != context)
            {
                _fitted.Clear();
                _context = context;
            }

            var catalog = context.Config != null ? context.Config.LoadoutCatalog : null;
            if (catalog == null) return;
            Index(catalog);
            var pool = context.ModuleVisuals;
            var skins = context.Config.Skins;

            _seen.Clear();
            foreach (var (loadout, pose, entity) in SystemAPI.Query<RefRO<ChopperLoadout>, RefRO<LocalToWorld>>().WithEntityAccess())
            {
                _seen.Add(entity);
                var code = loadout.ValueRO.Code;
                var skinRow = skins != null && SystemAPI.HasComponent<ChopperSkin>(entity)
                    ? skins.IndexOf(SystemAPI.GetComponent<ChopperSkin>(entity).SkinId)
                    : 0;
                if (!_fitted.TryGetValue(entity, out var fitted) || !fitted.Code.Equals(code) || fitted.SkinRow != skinRow)
                {
                    if (fitted != null) Release(pool, fitted);
                    fitted = Fit(pool, entity, code, skinRow);
                    _fitted[entity] = fitted;
                }

                var world = pose.ValueRO.Value;
                foreach (var (visual, position, rotation) in fitted.Parts)
                {
                    if (visual == null) continue;
                    visual.transform.SetPositionAndRotation(math.transform(world, position), math.mul(pose.ValueRO.Rotation, rotation));
                }
            }

            _gone.Clear();
            foreach (var entity in _fitted.Keys)
                if (!_seen.Contains(entity)) _gone.Add(entity);
            foreach (var entity in _gone)
            {
                Release(pool, _fitted[entity]);
                _fitted.Remove(entity);
            }
        }

        private Fitted Fit(EffectPool pool, Entity entity, in LoadoutCode code, int skinRow)
        {
            var fitted = new Fitted { Code = code, SkinRow = skinRow };
            if (!_chassis.TryGetValue(code.ChassisId, out var chassis) || !SystemAPI.HasBuffer<ChassisSocket>(entity)) return fitted;
            var sockets = SystemAPI.GetBuffer<ChassisSocket>(entity);

            var ordinals = new Dictionary<SlotType, int>();
            for (var slot = 0; slot < chassis.Slots.Count; slot++)
            {
                var slotType = chassis.Slots[slot];
                ordinals.TryGetValue(slotType, out var ordinal);
                ordinals[slotType] = ordinal + 1;

                if (!_modules.TryGetValue(code.GetModule(slot), out var module) || module.Visual == null) continue;
                if (!TryFindSocket(sockets, slotType, ordinal, out var socket)) continue;

                var visual = pool.Spawn(module.Visual, Vector3.zero, Quaternion.identity);
                DisableColliders(visual);
                SetSkin(visual, skinRow);
                fitted.Parts.Add((visual, socket.LocalPosition, socket.LocalRotation));
            }
            return fitted;
        }

        /// <summary>The runtime skin shader's row on every renderer of a module visual (other shaders ignore it).</summary>
        private void SetSkin(GameObject visual, int row)
        {
            if (visual == null) return;
            _block ??= new MaterialPropertyBlock();
            foreach (var target in visual.GetComponentsInChildren<Renderer>(true))
            {
                target.GetPropertyBlock(_block);
                _block.SetFloat(SkinLibrary.SkinIndexProperty, row);
                target.SetPropertyBlock(_block);
            }
        }

        /// <summary>Art prefabs often carry GameObject colliders; the simulation has its own, so these only cost time.</summary>
        private static void DisableColliders(GameObject visual)
        {
            if (visual == null) return;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        private static bool TryFindSocket(DynamicBuffer<ChassisSocket> sockets, SlotType slot, int index, out ChassisSocket found)
        {
            foreach (var socket in sockets)
            {
                if (socket.Slot != slot || socket.Index != index) continue;
                found = socket;
                return true;
            }
            found = default;
            return false;
        }

        private static void Release(EffectPool pool, Fitted fitted)
        {
            foreach (var (visual, _, _) in fitted.Parts) pool.Release(visual);
            fitted.Parts.Clear();
        }

        private void Index(LoadoutCatalog catalog)
        {
            if (_indexed == catalog) return;
            _indexed = catalog;
            _chassis.Clear();
            _modules.Clear();
            foreach (var chassis in catalog.Chassis) if (chassis != null) _chassis[chassis.Id] = chassis;
            foreach (var module in catalog.Modules) if (module != null) _modules[module.Id] = module;
        }
    }
}
