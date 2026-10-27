using Chopper.Simulation.Loadout;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    public struct ChopperSpawner : IComponentData
    {
        /// <summary>Used when a requested chassis has no prefab of its own.</summary>
        public Entity Prefab;
        public float3 Position;
        public quaternion Rotation;
        /// <summary>Sideways gap between players so they do not spawn inside each other.</summary>
        public float Spacing;
        /// <summary>Seconds between death and the new chopper.</summary>
        public float RespawnDelay;
        /// <summary>The guided missile ghost all guided weapons launch (visuals differ in presentation, not here).</summary>
        public Entity MissilePrefab;
    }

    /// <summary>One chopper ghost prefab per chassis (one ghost per chassis: own model, colliders, sockets).</summary>
    [InternalBufferCapacity(4)]
    public struct ChassisPrefab : IBufferElementData
    {
        public ushort ChassisId;
        public Entity Prefab;
    }

    /// <summary>
    /// The loadout catalog asset, for runtime loadout building (<see cref="LoadoutApplySystem"/>). Lives on the spawner
    /// in the gameplay subscene, so client and server worlds both have it.
    /// </summary>
    public struct LoadoutCatalogRef : IComponentData
    {
        public UnityObjectRef<LoadoutCatalog> Catalog;
    }

    /// <summary>Put in the gameplay subscene. Its transform is the first spawn point. Chopper → Setup fills it.</summary>
    public sealed class ChopperSpawnerAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Fallback chopper ghost when a chassis has no prefab.")] private GameObject chopperPrefab;
        [SerializeField] private float spacing = 20f;
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField, Tooltip("Chassis prefabs and module data for per-player loadouts.")] private LoadoutCatalog catalog;
        [SerializeField, Tooltip("Guided missile ghost prefab.")] private GameObject missilePrefab;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(LoadoutCatalog loadoutCatalog, GameObject missile)
        {
            if (loadoutCatalog != null) catalog = loadoutCatalog;
            if (missile != null) missilePrefab = missile;
        }

        public LoadoutCatalog Catalog => catalog;
        public GameObject MissilePrefab => missilePrefab;

        private sealed class Baker : Baker<ChopperSpawnerAuthoring>
        {
            public override void Bake(ChopperSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var transform = authoring.transform;
                AddComponent(entity, new ChopperSpawner
                {
                    Prefab = GetEntity(authoring.chopperPrefab, TransformUsageFlags.Dynamic),
                    Position = transform.position,
                    Rotation = transform.rotation,
                    Spacing = authoring.spacing,
                    RespawnDelay = authoring.respawnDelay,
                    MissilePrefab = authoring.missilePrefab != null ? GetEntity(authoring.missilePrefab, TransformUsageFlags.Dynamic) : Entity.Null,
                });

                // Collect first, add the buffer last: a later AddComponent would make the buffer handle invalid.
                var prefabs = new System.Collections.Generic.List<ChassisPrefab>();
                if (authoring.catalog != null)
                {
                    DependsOn(authoring.catalog);
                    AddComponent(entity, new LoadoutCatalogRef { Catalog = authoring.catalog });
                    foreach (var chassis in authoring.catalog.Chassis)
                    {
                        if (chassis == null || chassis.Prefab == null) continue;
                        DependsOn(chassis);
                        prefabs.Add(new ChassisPrefab { ChassisId = chassis.Id, Prefab = GetEntity(chassis.Prefab, TransformUsageFlags.Dynamic) });
                    }
                }

                var buffer = AddBuffer<ChassisPrefab>(entity);
                foreach (var prefab in prefabs) buffer.Add(prefab);
            }
        }
    }
}
