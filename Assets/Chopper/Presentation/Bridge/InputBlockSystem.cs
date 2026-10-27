using Chopper.Simulation.Input;
using Unity.Entities;

namespace Chopper.Presentation.Bridge
{
    /// <summary>
    /// Client only. Copies <see cref="PresentationContext.MenuOpen"/> into the <see cref="PlayerInputBlock"/> singleton,
    /// so simulation input stops while a menu is open (simulation cannot read presentation).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InputBlockSystem : PresentationSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            EntityManager.CreateSingleton<PlayerInputBlock>("PlayerInputBlock");
        }

        protected override void OnUpdate()
        {
            var blocked = TryGetContext(out var context) && context.MenuOpen;
            ref var block = ref SystemAPI.GetSingletonRW<PlayerInputBlock>().ValueRW;
            block.Blocked = blocked;
        }
    }
}
