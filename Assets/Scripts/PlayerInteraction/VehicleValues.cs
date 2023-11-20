using System;
using Unity.Netcode;
using UnityEngine;

namespace PlayerInteraction
{
    [Serializable]
    public struct VehicleValues : IEquatable<VehicleValues>
    {
        public float yawTorque, pitchTorque, rollTorque,thrustForce;
        [Range(-1, 1)]public float thrustVectorOffset;
        
        public VehicleValues(float yawTorque, float pitchTorque, float rollTorque, float thrustForce, float thrustVectorOffset)
        {
            this.yawTorque = yawTorque;
            this.pitchTorque = pitchTorque;
            this.rollTorque = rollTorque;
            this.thrustForce = thrustForce;
            this.thrustVectorOffset = Mathf.Clamp(thrustVectorOffset, -1, 1 );
        }
        
        public bool Equals(VehicleValues other)
        {
            return yawTorque.Equals(other.yawTorque) && 
                   pitchTorque.Equals(other.pitchTorque) && 
                   rollTorque.Equals(other.rollTorque) && 
                   thrustForce.Equals(other.thrustForce) && 
                   thrustVectorOffset.Equals(other.thrustVectorOffset);
        }

        // public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        // {
        //     serializer.SerializeValue(ref yawTorque);
        //     serializer.SerializeValue(ref pitchTorque);
        //     serializer.SerializeValue(ref rollTorque);
        //     serializer.SerializeValue(ref thrustForce);
        //     serializer.SerializeValue(ref thrustVectorOffset);
        // }
    }
}