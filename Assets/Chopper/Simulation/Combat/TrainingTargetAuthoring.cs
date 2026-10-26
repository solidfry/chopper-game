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
    /// The target is not a ghost, so only the server tracks its health; the shooter sees the hit markers.
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
            }
        }
    }
}
