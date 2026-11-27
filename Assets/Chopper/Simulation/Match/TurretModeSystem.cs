using Chopper.Simulation.Combat;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Chopper.Simulation.Match
{
    /// <summary>
    /// Server only. The pad turrets belong to Capture the Nuke (owner, 26 Nov): in any other mode they are removed when
    /// the match state is known. They are ghosts, so clients lose them too. The maps keep them for CTN.
    /// </summary>
    [BurstCompile]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct TurretModeSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MatchState>();
            state.RequireForUpdate<TurretConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!ModeHasTurrets(SystemAPI.GetSingleton<MatchState>().Mode))
            {
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                foreach (var (_, entity) in SystemAPI.Query<RefRO<TurretConfig>>().WithEntityAccess()) ecb.DestroyEntity(entity);
                ecb.Playback(state.EntityManager);
                ecb.Dispose();
            }
        }

        /// <summary>True for the modes that use the pad turrets.</summary>
        public static bool ModeHasTurrets(MatchMode mode) => mode == MatchMode.CaptureTheNuke;
    }
}
