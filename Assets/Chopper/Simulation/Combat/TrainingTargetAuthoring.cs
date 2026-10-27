using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Combat
{
    /// <summary>Server only: a target that comes back to full health some time after it is destroyed.</summary>
    public struct TrainingTarget : IComponentData
    {
        public float ResetDelay;
        public float ResetRemaining;
    }

    /// <summary>
    /// Put on a target in the gameplay subscene (it needs a collider). Lets one player test hits and kills.
    /// Chopper → Setup also makes it a pre-spawned ghost, so clients see its health and seekers can show a lock on it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingTargetAuthoring : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField, Tooltip("Seconds after a kill before the target is back to full health.")]
        private float resetDelay = 3f;

        private sealed class Baker : Baker<TrainingTargetAuthoring>
        {
            public override void Bake(TrainingTargetAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent(entity, new Health { Current = authoring.maxHealth, Max = authoring.maxHealth });
                AddBuffer<DamageEvent>(entity);
                AddComponent(entity, new TrainingTarget { ResetDelay = authoring.resetDelay });
                // Ground class: air-to-ground seekers can lock it. Warmer than the ground so it stands out.
                AddComponent(entity, new Targetable { Class = TargetClass.Ground, BaseHeat = 0.6f });
            }
        }
    }
}
