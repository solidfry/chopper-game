using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Combat
{
    /// <summary>
    /// Put on the missile ghost prefab (with a GhostAuthoringComponent: owner-predicted, has owner). The launcher fills
    /// in the <see cref="GuidedMissile"/> values at spawn; visuals come from presentation, so the prefab needs no mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuidedMissileAuthoring : MonoBehaviour
    {
        private sealed class Baker : Baker<GuidedMissileAuthoring>
        {
            public override void Bake(GuidedMissileAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<GuidedMissile>(entity);
            }
        }
    }
}
