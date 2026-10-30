using Chopper.Presentation.Bridge;
using Chopper.Simulation.Loadout;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Client only. In a cockpit view (<see cref="Camera.ChopperCameraRig.HidesOwnChopper"/>) the local player sees the
    /// cockpit interior of their chassis (<see cref="ChassisDefinition.Cockpit"/>, following the chopper's rendered pose)
    /// instead of their own hull: the hull's rendered entities get <see cref="DisableRendering"/> (outside details
    /// would float in the view; the pilot sees out through a camera feed). <see cref="ModuleVisualSystem"/> hides the
    /// module art the same way. Remote choppers never change, so it costs nothing per player. The cockpit comes from
    /// the module visual pool (<see cref="PresentationContext.ModuleVisuals"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CockpitVisualSystem : PresentationSystemBase
    {
        private readonly ChassisIndex _chassis = new();
        private PresentationContext _context;
        private GameObject _source;
        private GameObject _instance;
        /// <summary>The chopper whose hull is hidden now (Entity.Null = none).</summary>
        private Entity _hidden;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            SetHidden(_hidden, false);
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_context != context)
            {
                // A new context has a new pool: the old instance went with the old one.
                _context = context;
                _source = null;
                _instance = null;
            }

            var catalog = context.Config != null ? context.Config.LoadoutCatalog : null;
            GameObject wanted = null;
            var local = Entity.Null;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            foreach (var (loadout, pose, entity) in SystemAPI.Query<RefRO<ChopperLoadout>, RefRO<LocalToWorld>>()
                         .WithAll<GhostOwnerIsLocal>().WithEntityAccess())
            {
                if (_chassis.TryGet(catalog, loadout.ValueRO.ChassisId, out var chassis)) wanted = chassis.Cockpit;
                local = entity;
                position = pose.ValueRO.Position;
                rotation = pose.ValueRO.Rotation;
                break;
            }

            var cockpitView = context.CameraRig != null && context.CameraRig.HidesOwnChopper;
            var hide = cockpitView && wanted != null ? local : Entity.Null;
            if (hide != _hidden)
            {
                SetHidden(_hidden, false);
                SetHidden(hide, true);
                _hidden = hide;
            }

            if (wanted != _source)
            {
                if (_instance != null) context.ModuleVisuals.Release(_instance);
                _source = wanted;
                _instance = context.ModuleVisuals.Spawn(wanted, position, rotation);
            }
            if (_instance == null) return;

            if (_instance.activeSelf != cockpitView) _instance.SetActive(cockpitView);
            if (cockpitView) _instance.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Hides or shows every rendered entity of a chopper (its linked entity group).</summary>
        private void SetHidden(Entity chopper, bool hidden)
        {
            if (chopper == Entity.Null || !EntityManager.Exists(chopper) || !EntityManager.HasBuffer<LinkedEntityGroup>(chopper)) return;
            // Copy first: adding a component is a structural change and invalidates the buffer.
            var group = EntityManager.GetBuffer<LinkedEntityGroup>(chopper).Reinterpret<Entity>().ToNativeArray(Allocator.Temp);
            foreach (var entity in group)
            {
                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<MaterialMeshInfo>(entity)) continue;
                if (hidden) EntityManager.AddComponent<DisableRendering>(entity);
                else EntityManager.RemoveComponent<DisableRendering>(entity);
            }
            group.Dispose();
        }
    }
}
