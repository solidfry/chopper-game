using System;
using UnityEngine;

namespace PlayerInteraction
{
    [Serializable]
    public struct VehicleValues : IEquatable<VehicleValues>, IFormattable
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

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return yawTorque.ToString(format, formatProvider) + ", " + pitchTorque.ToString(format, formatProvider) + ", " + rollTorque.ToString(format, formatProvider) + ", " + thrustForce.ToString(format, formatProvider)  + ", " + thrustVectorOffset.ToString(format, formatProvider);
        }
    }
}