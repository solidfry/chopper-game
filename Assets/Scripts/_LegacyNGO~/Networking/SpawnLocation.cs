using System;
using System.Collections.Generic;
using UnityEngine;

namespace Networking
{
    [Serializable]
    public record SpawnLocation
    {
        // [SerializeField] public Transform currentPlayerTransform;
        [field: SerializeField] public Transform Transform { get; private set; } = null;
        [field: SerializeField] public bool IsUsed { get; private set; } = false;

        public Transform GetAndUsePosition()
        {
            IsUsed = true;
            return Transform;
        }

        public void ReleasePosition() => IsUsed = false;

        public bool IsPositionUsed()
        {
            return IsUsed;
        }
    }

    [Serializable]
    class SpawnLocations
    {
        [SerializeField]
        public List<SpawnLocation> spawnLocations;
        
        public Transform GetNextUnusedPosition()
        {
            foreach (var location in spawnLocations)
                if (!location.IsPositionUsed())
                    return location.GetAndUsePosition();

            return null;
        }
        
        public Transform GetRandomUnusedPosition()
        {
            var unusedLocations = spawnLocations.FindAll(location => !location.IsPositionUsed());
            if (unusedLocations.Count == 0) return null;
            var randomIndex = UnityEngine.Random.Range(0, unusedLocations.Count);
            return unusedLocations[randomIndex].GetAndUsePosition();
        }

        public void ReleasePositionAtIndex(int index) => spawnLocations[index].ReleasePosition();

        public void ReleasePositionByTransform(Transform transform) => spawnLocations.Find(location => location.Transform == transform).ReleasePosition();

        public void ReleaseAllPositions() => spawnLocations.ForEach(location => location.ReleasePosition());

        public bool AllPositionsUsed() => spawnLocations.TrueForAll(location => location.IsPositionUsed());

    }
}