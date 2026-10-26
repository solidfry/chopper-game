using Chopper.Presentation.Camera;
using Chopper.Presentation.Hud;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Hits taken (<see cref="DamageTakenRpc"/>, sent only to the victim) shake the camera and show
    /// where the hit came from on the HUD.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class DamageFeedbackSystem : SystemBase
    {
        /// <summary>Damage that gives full camera trauma in one hit.</summary>
        private const float FullTraumaDamage = 40f;

        private ChopperCameraRig _rig;
        private ChopperHudModelHost _hud;
        private EntityQuery _damageTaken;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkStreamInGame>();
            _damageTaken = SystemAPI.QueryBuilder().WithAll<DamageTakenRpc, ReceiveRpcCommandRequest>().Build();
        }

        protected override void OnUpdate()
        {
            if (_damageTaken.IsEmpty) return;
            if (_rig == null) _rig = Object.FindFirstObjectByType<ChopperCameraRig>();
            if (_hud == null) _hud = Object.FindFirstObjectByType<ChopperHudModelHost>();

            foreach (var damage in SystemAPI.Query<RefRO<DamageTakenRpc>>().WithAll<ReceiveRpcCommandRequest>())
            {
                if (_rig != null)
                    _rig.AddShake(Mathf.Clamp01(damage.ValueRO.Amount / FullTraumaDamage));

                if (_hud != null && _rig != null && damage.ValueRO.HasSource)
                    _hud.Model.ShowDamageFrom(DirectionFromCamera(_rig.transform, damage.ValueRO.SourcePosition));
            }
            EntityManager.DestroyEntity(_damageTaken);
        }

        /// <summary>Angle round the camera's view axis: 0 = ahead, 90 = right, 180 = behind.</summary>
        private static float DirectionFromCamera(Transform camera, Vector3 source)
        {
            var local = camera.InverseTransformDirection(source - camera.position);
            return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        }
    }
}
