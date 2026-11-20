using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Match
{
    // Own file: Unity keeps a scene reference to a MonoBehaviour only when its file has the class name (it was in
    // NukeAuthoring.cs and the scenes lost it on reload).
    /// <summary>A team's base zone. Chopper → Setup step 4 adds one per team.</summary>
    [DisallowMultipleComponent]
    public sealed class NukeBaseAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("1.. (team A = 1).")] private byte team = 1;
        [SerializeField] private float radius = 45f;

        public byte Team { get => team; set => team = value; }

        private sealed class Baker : Baker<NukeBaseAuthoring>
        {
            public override void Bake(NukeBaseAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NukeBase { Team = authoring.team, Radius = authoring.radius, Position = authoring.transform.position });
            }
        }
    }
}
