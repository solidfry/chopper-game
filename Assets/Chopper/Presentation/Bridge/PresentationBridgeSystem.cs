using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>Singleton in the client world: the scene's presentation bridge (unmanaged, holds an object reference).</summary>
    public struct PresentationBridgeRef : IComponentData
    {
        public UnityObjectRef<PresentationBridge> Bridge;
    }

    /// <summary>
    /// Client only. Finds the scene's <see cref="PresentationBridge"/> (the only scene search in presentation code)
    /// and puts a reference to it in the world. Re-binds after a scene change.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class PresentationBridgeSystem : SystemBase
    {
        private const double RetrySeconds = 1.0;

        private EntityQuery _bridgeQuery;
        private double _nextSearch;

        protected override void OnCreate()
        {
            _bridgeQuery = GetEntityQuery(ComponentType.ReadOnly<PresentationBridgeRef>());
        }

        protected override void OnUpdate()
        {
            if (!_bridgeQuery.IsEmpty)
            {
                if (PresentationSystemBase.IsUsable(_bridgeQuery.GetSingleton<PresentationBridgeRef>())) return;
                EntityManager.DestroyEntity(_bridgeQuery);
            }

            // No bridge yet (scene loading, or a scene without presentation): look again later, not every frame.
            var now = SystemAPI.Time.ElapsedTime;
            if (now < _nextSearch) return;
            _nextSearch = now + RetrySeconds;

            var bridge = Object.FindAnyObjectByType<PresentationBridge>();
            if (bridge == null) return;

            var holder = EntityManager.CreateEntity();
            EntityManager.SetName(holder, "PresentationBridge");
            EntityManager.AddComponentData(holder, new PresentationBridgeRef { Bridge = bridge });
        }
    }

    /// <summary>
    /// Base for client presentation systems: <see cref="TryGetContext"/> gives the scene's context, or false when
    /// there is none. Derived systems call <c>base.OnCreate()</c>.
    /// </summary>
    public abstract partial class PresentationSystemBase : SystemBase
    {
        private EntityQuery _bridgeQuery;

        protected override void OnCreate()
        {
            _bridgeQuery = GetEntityQuery(ComponentType.ReadOnly<PresentationBridgeRef>());
        }

        protected bool TryGetContext(out PresentationContext context)
        {
            context = null;
            if (_bridgeQuery.IsEmpty) return false;
            var reference = _bridgeQuery.GetSingleton<PresentationBridgeRef>();
            if (!IsUsable(reference)) return false;
            context = reference.Bridge.Value.Context;
            return true;
        }

        /// <summary>False once the bridge object is destroyed (scene unload) or its context is disposed.</summary>
        internal static bool IsUsable(in PresentationBridgeRef reference)
        {
            var bridge = reference.Bridge.Value;
            return bridge != null && !bridge.Context.IsDisposed;
        }
    }
}
