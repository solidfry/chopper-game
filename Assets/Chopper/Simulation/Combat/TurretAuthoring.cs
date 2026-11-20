using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// A gun turret in the gameplay subscene (needs a collider). Shoots choppers in range; destroyed, it comes back after
    /// the respawn time. Like <see cref="TrainingTargetAuthoring"/>, it must be a pre-spawned ghost so clients see its
    /// health, barrel and down state. The forward axis is the start aim.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TurretAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Health with full teams (the cap).")] private float maxHealth = 240f;
        [SerializeField, Tooltip("Health per pilot in the bigger team (0 = always max).")] private float healthPerPilot = 60f;
        [SerializeField, Tooltip("Health floor (1 pilot a side).")] private float minHealth = 80f;
        [SerializeField, Tooltip("Seconds a destroyed turret stays down.")] private float respawnSeconds = 45f;
        [SerializeField, Tooltip("0 = neutral (shoots every chopper). 1.. = a team: shoots only other teams.")]
        private byte team;

        [Header("Gun")]
        [SerializeField] private float range = 260f;
        [SerializeField] private float fireInterval = 0.15f;
        [SerializeField] private float damage = 4f;
        [SerializeField] private float bulletSpeed = 450f;
        [SerializeField, Tooltip("Damage starts to fall here, metres (0 = no falloff).")] private float falloffStart = 150f;
        [SerializeField, Range(0f, 1f)] private float falloffFloor = 0.5f;
        [SerializeField, Tooltip("Gun position in turret space.")] private Vector3 muzzleOffset = new Vector3(0f, 2.5f, 0f);

        [SerializeField, Tooltip("Trunnion to barrel tip, metres: rounds and tracers start here.")] private float barrelLength = 2.4f;

        [Header("Rig (optional: turned on clients from the replicated aim)")]
        [SerializeField, Tooltip("Turns left and right (yaw). Child of this object.")] private Transform head;
        [SerializeField, Tooltip("Tilts up and down (pitch). Child of the head.")] private Transform guns;

        [Header("Aim")]
        [SerializeField, Tooltip("Degrees per second.")] private float turnRate = 70f;
        [SerializeField, Tooltip("Fires when the barrel is this close to the lead point, degrees.")] private float fireCone = 4f;
        [SerializeField, Tooltip("Random aim error per shot, degrees.")] private float spread = 1.5f;

        private sealed class Baker : Baker<TurretAuthoring>
        {
            public override void Bake(TurretAuthoring a)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Health { Current = a.maxHealth, Max = a.maxHealth });
                AddBuffer<DamageEvent>(entity);
                AddBuffer<DamageOverTime>(entity);
                AddComponent<StatusLook>(entity);
                AddComponent(entity, new Targetable { Class = TargetClass.Ground, BaseHeat = 0.7f });
                AddComponent(entity, new TurretConfig
                {
                    Gun = new HitscanWeaponConfig
                    {
                        FireInterval = math.max(a.fireInterval, 0.02f),
                        Damage = a.damage,
                        Range = a.range,
                        FalloffStart = a.falloffStart,
                        FalloffFloor = a.falloffFloor,
                        MuzzleOffset = a.muzzleOffset,
                        BulletSpeed = math.max(a.bulletSpeed, 1f),
                        Name = "TURRET",
                    },
                    BarrelLength = a.barrelLength,
                    TurnRate = math.radians(a.turnRate),
                    FireCone = math.radians(a.fireCone),
                    Spread = math.radians(a.spread),
                    RespawnSeconds = a.respawnSeconds,
                    Team = a.team,
                    HealthPerPilot = a.healthPerPilot,
                    MinHealth = math.min(a.minHealth, a.maxHealth),
                    MaxHealth = a.maxHealth,
                });
                AddComponent(entity, new TurretState { Aim = a.transform.forward });
                if (a.head != null && a.guns != null)
                    AddComponent(entity, new TurretRig
                    {
                        Head = GetEntity(a.head, TransformUsageFlags.Dynamic),
                        Guns = GetEntity(a.guns, TransformUsageFlags.Dynamic),
                    });
            }
        }
    }
}
