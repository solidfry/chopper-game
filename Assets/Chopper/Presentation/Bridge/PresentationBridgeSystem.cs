using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>Managed singleton in the client world: the scene's presentation context.</summary>
    public sealed class PresentationContextRef : IComponentData
    {
        public PresentationContext Value;
    }

    /// <summary>
    /// Client only. Finds the scene's <see cref="PresentationBridge"/> (the only scene search in presentation code)
    /// and puts its context in the world. Re-binds after a scene change.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
    public partial class PresentationBridgeSystem : SystemBase
    {
        private const double RetrySeconds = 1.0;

        private EntityQuery _contextQuery;
        private double _nextSearch;

        protected override void OnCreate()
        {
            _contextQuery = GetEntityQuery(ComponentType.ReadOnly<PresentationContextRef>());
        }

        protected override void OnUpdate()
        {
            if (!_contextQuery.IsEmpty)
            {
                var entity = _contextQuery.GetSingletonEntity();
                var current = EntityManager.GetComponentObject<PresentationContextRef>(entity);
                if (current.Value != null && !current.Value.IsDisposed) return;
                EntityManager.DestroyEntity(entity);
            }

            // No bridge yet (scene loading, or a scene without presentation): look again later, not every frame.
            var now = SystemAPI.Time.ElapsedTime;
            if (now < _nextSearch) return;
            _nextSearch = now + RetrySeconds;

            var bridge = Object.FindFirstObjectByType<PresentationBridge>();
            if (bridge == null) return;

            var holder = EntityManager.CreateEntity();
            EntityManager.SetName(holder, "PresentationContext");
            EntityManager.AddComponentObject(holder, new PresentationContextRef { Value = bridge.Context });
        }
    }

    /// <summary>
    /// Base for client presentation systems: <see cref="TryGetContext"/> gives the scene's context, or false when
    /// there is none. Derived systems call <c>base.OnCreate()</c>.
    /// </summary>
    public abstract partial class PresentationSystemBase : SystemBase
    {
        private EntityQuery _contextQuery;

        protected override void OnCreate()
        {
            _contextQuery = GetEntityQuery(ComponentType.ReadOnly<PresentationContextRef>());
        }

        protected bool TryGetContext(out PresentationContext context)
        {
            context = null;
            if (_contextQuery.IsEmpty) return false;
            context = EntityManager.GetComponentObject<PresentationContextRef>(_contextQuery.GetSingletonEntity()).Value;
            return context != null && !context.IsDisposed;
        }
    }
}
