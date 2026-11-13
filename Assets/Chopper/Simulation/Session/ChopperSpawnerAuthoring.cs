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
        /// <summary>
        /// The chassis of <see cref="Prefab"/>'s default loadout. A player with no loadout choice gets this chassis's own
        /// prefab (its kit model), not the placeholder.
        /// </summary>
        public ushort DefaultChassisId;
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

    /// <summary>
    /// Put in the gameplay subscene. Chopper → Setup fills it. Choppers spawn at the map's <see cref="SpawnPointAuthoring"/>
    /// points (rules: <see cref="SpawnSettings"/>); this transform is only the fallback when none of them can be used.
    /// </summary>
    public sealed class ChopperSpawnerAuthoring : MonoBehaviour
    {
        [SerializeField, Tooltip("Fallback chopper ghost when a chassis has no prefab.")] private GameObject chopperPrefab;
        [SerializeField] private float spacing = 20f;
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField, Tooltip("Chassis prefabs and module data for per-player loadouts.")] private LoadoutCatalog catalog;
        [SerializeField, Tooltip("Guided missile ghost prefab.")] private GameObject missilePrefab;
        [SerializeField, Tooltip("How bots fly on this map (BotHandling singleton).")] private Bots.BotProfile botProfile;
        [SerializeField, Tooltip("How the server picks a spawn point (SpawnRules singleton). Empty = default values.")]
        private SpawnSettings spawnSettings;
        [SerializeField, Tooltip("Server bots that fill empty slots (BotFillRules, roster). Empty = no server bots on this map.")]
        private Bots.BotSettings botSettings;
        [SerializeField, Tooltip("Level curves and the lock switch for the server's unlock check. Empty = nothing is locked on this map.")]
        private Progression.ProgressionSettings progressionSettings;
        [SerializeField, Tooltip("Account level each paint skin needs. Setup step 4 copies it from the skin library.")]
        private Progression.SkinUnlockRow[] skinUnlocks = System.Array.Empty<Progression.SkinUnlockRow>();

        /// <summary>Editor only: used by the setup tool. Every call replaces the skin list (it is a copy of the library).</summary>
        public void ConfigureProgression(Progression.ProgressionSettings settings, Progression.SkinUnlockRow[] skins)
        {
            if (settings != null) progressionSettings = settings;
            skinUnlocks = skins ?? System.Array.Empty<Progression.SkinUnlockRow>();
        }

        public Progression.ProgressionSettings ProgressionSettings => progressionSettings;

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Configure(LoadoutCatalog loadoutCatalog, GameObject missile, Bots.BotProfile bots, SpawnSettings spawn,
            Bots.BotSettings botFill = null)
        {
            if (botFill != null) botSettings = botFill;
            if (spawn != null) spawnSettings = spawn;
            if (loadoutCatalog != null) catalog = loadoutCatalog;
            if (missile != null) missilePrefab = missile;
            if (bots != null) botProfile = bots;
        }

        public LoadoutCatalog Catalog => catalog;
        public GameObject MissilePrefab => missilePrefab;
        public Bots.BotProfile BotProfile => botProfile;
        public SpawnSettings SpawnSettings => spawnSettings;
        public Bots.BotSettings BotSettings => botSettings;

        private sealed class Baker : Baker<ChopperSpawnerAuthoring>
        {
            public override void Bake(ChopperSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var transform = authoring.transform;
                var defaultChassis = authoring.chopperPrefab != null &&
                                     authoring.chopperPrefab.TryGetComponent<Flight.ChopperAuthoring>(out var fallback)
                    ? fallback.Chassis
                    : null;
                if (defaultChassis != null) DependsOn(defaultChassis);
                AddComponent(entity, new ChopperSpawner
                {
                    Prefab = GetEntity(authoring.chopperPrefab, TransformUsageFlags.Dynamic),
                    DefaultChassisId = defaultChassis != null ? defaultChassis.Id : (ushort)0,
                    Position = transform.position,
                    Rotation = transform.rotation,
                    Spacing = authoring.spacing,
                    RespawnDelay = authoring.respawnDelay,
                    MissilePrefab = authoring.missilePrefab != null ? GetEntity(authoring.missilePrefab, TransformUsageFlags.Dynamic) : Entity.Null,
                });

                if (authoring.spawnSettings != null) DependsOn(authoring.spawnSettings);
                AddComponent(entity, authoring.spawnSettings != null ? authoring.spawnSettings.ToRules() : SpawnRules.Default);

                if (authoring.botProfile != null)
                {
                    DependsOn(authoring.botProfile);
                    AddComponent(entity, authoring.botProfile.ToHandling());
                }

                // Progression: the server's unlock check (curves, lock switch, skin levels).
                if (authoring.progressionSettings != null)
                {
                    DependsOn(authoring.progressionSettings);
                    AddComponent(entity, authoring.progressionSettings.ToRules());
                    var skinLevels = AddBuffer<Progression.SkinUnlock>(entity);
                    foreach (var row in authoring.skinUnlocks)
                        skinLevels.Add(new Progression.SkinUnlock { SkinId = row.SkinId, Level = row.Level });
                }

                // Server bots: the count rules and the roster (names, loadouts, skill).
                var rosterRows = new System.Collections.Generic.List<Bots.BotRosterEntry>();
                if (authoring.botSettings != null)
                {
                    DependsOn(authoring.botSettings);
                    AddComponent(entity, authoring.botSettings.ToRules());
                    var rows = authoring.botSettings.Roster;
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        if (row == null) continue;
                        if (row.Profile != null) DependsOn(row.Profile);
                        if (row.Chassis != null) DependsOn(row.Chassis);
                        var name = string.IsNullOrWhiteSpace(row.Name) ? Bots.BotNames.Fallback : row.Name.Trim();
                        if (name.Length > Bots.BotSettings.MaxNameLength) name = name.Substring(0, Bots.BotSettings.MaxNameLength);
                        rosterRows.Add(new Bots.BotRosterEntry
                        {
                            Name = name,
                            Loadout = row.ToCode(authoring.catalog, i),
                            HasHandling = row.Profile != null,
                            Handling = row.Profile != null ? row.Profile.ToHandling() : default,
                        });
                    }
                }

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

                var roster = AddBuffer<Bots.BotRosterEntry>(entity);
                foreach (var row in rosterRows) roster.Add(row);
            }
        }
    }
}
