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
        [SerializeField, Tooltip("Base zone radius, m (a facility: landing bays round the dock; owner, 25 Nov).")]
        private float radius = 120f;
        [SerializeField, Tooltip("The nuke dock, in this object's space: the carrier sets the nuke down here to arm it.")]
        private Vector3 dockOffset = Vector3.zero;

        public byte Team { get => team; set => team = value; }
        public float Radius { get => radius; set => radius = value; }
        public Vector3 Dock => transform.TransformPoint(dockOffset);

        private sealed class Baker : Baker<NukeBaseAuthoring>
        {
            public override void Bake(NukeBaseAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new NukeBase { Team = authoring.team, Radius = authoring.radius, Position = authoring.transform.position, Dock = authoring.Dock });
            }
        }
    }
}
