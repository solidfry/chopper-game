using System;
using UnityEngine;

namespace Networking
{
    [Serializable]
    public record SpawnLocation
    {
        [field: SerializeField] public Transform Transform { get; private set; } = null;
        [field: SerializeField] public bool IsUsed { get; private set; } = false;
        
        public Transform UsePosition()
        {
            IsUsed = true;
            return Transform;
        }
        
        public void ReleasePosition()
        {
            IsUsed = false;
        }
        
        public bool IsPositionUsed()
        {
            return IsUsed;
        }
    }
}