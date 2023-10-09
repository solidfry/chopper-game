using System;
using UnityEngine;

namespace Networking
{
    [Serializable]
    public record SpawnLocation(Transform Transform, bool IsUsed)
    {
        [field:SerializeField] public Transform Transform { get; private set; }
        [field:SerializeField] public bool IsUsed { get; private set; }   
        
        public Transform UsePosition()
        {
            IsUsed = true;
            return this.Transform;
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