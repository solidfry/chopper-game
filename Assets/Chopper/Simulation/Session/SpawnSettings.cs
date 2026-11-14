using System.Collections.Generic;
using Chopper.Simulation.Config;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Simulation.Session
{
    /// <summary>
    /// How the server picks a spawn point (<see cref="SpawnScoring"/>). One asset, linked from the GameConfig; the
    /// gameplay subscene's spawner bakes it into a <see cref="SpawnRules"/> singleton (Chopper → Setup, steps 1 and 4).
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Spawn Settings", fileName = "SpawnSettings")]
    public sealed class SpawnSettings : ScriptableObject, IValidatedAsset
    {
        [Header("Maps")]
        [SerializeField, Min(1), Tooltip("Validate lists a map with fewer spawn points than this.")]
        private int minPoints = 12;
        [SerializeField, Min(1), Tooltip("Spawn points Setup step 10 makes on each city map.")]
        private int cityPoints = 12;

        [Header("Walls and height (a point that fails is not used)")]
        [SerializeField, Min(0f), Tooltip("No level geometry nearer than this, m.")]
        private float wallClearance = 12f;
        [SerializeField, Min(0f), Tooltip("Lowest height over the ground, m.")]
        private float minGroundHeight = 15f;
        [SerializeField, Min(0f), Tooltip("Highest height over the ground, m. 0 = no limit.")]
        private float maxGroundHeight;
        [SerializeField, Tooltip("Open sky above the point (not under a roof or inside a building).")]
        private bool needOpenSky = true;

        [Header("Other choppers")]
        [SerializeField, Min(0f), Tooltip("No enemy nearer than this, m. Ignored only when no point passes.")]
        private float minEnemyDistance = 150f;
        [SerializeField, Min(0f), Tooltip("No chopper of any side nearer than this, m (never ignored).")]
        private float occupiedRadius = 25f;
        [SerializeField, Min(0f), Tooltip("An enemy this far or farther gives the full distance score, m.")]
        private float preferredEnemyDistance = 600f;
        [SerializeField, Min(0f), Tooltip("An enemy farther than this cannot see the point, m.")]
        private float sightRange = 900f;

        [Header("Score (higher wins)")]
        [SerializeField, Min(0f), Tooltip("Weight: distance to the nearest enemy.")]
        private float enemyDistanceWeight = 1f;
        [SerializeField, Min(0f), Tooltip("Weight: no enemy has line of sight to the point.")]
        private float outOfSightWeight = 0.5f;
        [SerializeField, Min(0f), Tooltip("Weight: free space round the point.")]
        private float clearanceWeight = 0.25f;
        [SerializeField, Min(0f), Tooltip("Free space that gives the full clearance score, m.")]
        private float preferredClearance = 40f;
        [SerializeField, Min(0f), Tooltip("Weight (minus): the point was used a short time ago.")]
        private float reuseWeight = 0.5f;
        [SerializeField, Min(0f), Tooltip("Seconds until a used point has no penalty.")]
        private float reuseSeconds = 10f;

        public int MinPoints => minPoints;
        public int CityPoints => cityPoints;
        public float WallClearance => wallClearance;

        public SpawnRules ToRules() => new()
        {
            WallClearance = wallClearance,
            MinGroundHeight = minGroundHeight,
            MaxGroundHeight = maxGroundHeight,
            NeedOpenSky = needOpenSky,
            MinEnemyDistance = minEnemyDistance,
            OccupiedRadius = occupiedRadius,
            PreferredEnemyDistance = Mathf.Max(preferredEnemyDistance, minEnemyDistance + 1f),
            SightRange = sightRange,
            EnemyDistanceWeight = enemyDistanceWeight,
            OutOfSightWeight = outOfSightWeight,
            ClearanceWeight = clearanceWeight,
            PreferredClearance = Mathf.Max(preferredClearance, wallClearance + 1f),
            ReuseWeight = reuseWeight,
            ReuseSeconds = reuseSeconds,
        };

        public void CollectProblems(List<string> problems)
        {
            if (cityPoints < minPoints) problems.Add($"{name}: city points ({cityPoints}) is less than the minimum ({minPoints}).");
            if (maxGroundHeight > 0f && maxGroundHeight < minGroundHeight)
                problems.Add($"{name}: the highest ground height is below the lowest.");
            if (preferredEnemyDistance <= minEnemyDistance)
                problems.Add($"{name}: the preferred enemy distance must be more than the minimum enemy distance.");
        }
    }

    /// <summary>Unmanaged copy of <see cref="SpawnSettings"/>. A singleton on the spawner.</summary>
    public struct SpawnRules : IComponentData
    {
        public float WallClearance;
        public float MinGroundHeight;
        /// <summary>0 = no limit.</summary>
        public float MaxGroundHeight;
        public bool NeedOpenSky;
        public float MinEnemyDistance;
        public float OccupiedRadius;
        public float PreferredEnemyDistance;
        public float SightRange;
        public float EnemyDistanceWeight;
        public float OutOfSightWeight;
        public float ClearanceWeight;
        public float PreferredClearance;
        public float ReuseWeight;
        public float ReuseSeconds;

        /// <summary>The asset's default values (a spawner with no settings asset bakes these).</summary>
        public static SpawnRules Default => new()
        {
            WallClearance = 12f,
            MinGroundHeight = 15f,
            MaxGroundHeight = 0f,
            NeedOpenSky = true,
            MinEnemyDistance = 150f,
            OccupiedRadius = 25f,
            PreferredEnemyDistance = 600f,
            SightRange = 900f,
            EnemyDistanceWeight = 1f,
            OutOfSightWeight = 0.5f,
            ClearanceWeight = 0.25f,
            PreferredClearance = 40f,
            ReuseWeight = 0.5f,
            ReuseSeconds = 10f,
        };
    }
}
