#if UNITY_EDITOR
using Chopper.Simulation.Bots;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// Editor only: draws each bot's path in the Scene view (green = to the next waypoint, yellow = the rest, red = the
    /// goal when no path was found). Debug lines only, so nothing shows in the Game view or builds.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BotPathDebugSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<BotNavigator>();

        protected override void OnUpdate()
        {
            foreach (var (pilot, navigator, waypoints) in SystemAPI.Query<RefRO<BotPilotState>, RefRO<BotNavigator>, DynamicBuffer<BotWaypoint>>())
            {
                var chopper = pilot.ValueRO.Chopper;
                if (!SystemAPI.HasComponent<LocalTransform>(chopper)) continue;
                Vector3 from = SystemAPI.GetComponent<LocalTransform>(chopper).Position;
                var nav = navigator.ValueRO;
                if (nav.Status == NavPathStatus.None || waypoints.Length == 0)
                {
                    Debug.DrawLine(from, nav.Goal, Color.red);
                    continue;
                }
                for (var i = nav.Next; i < waypoints.Length; i++)
                {
                    Vector3 to = waypoints[i].Position;
                    Debug.DrawLine(from, to, i == nav.Next ? Color.green : Color.yellow);
                    from = to;
                }
            }
        }
    }
}
#endif
