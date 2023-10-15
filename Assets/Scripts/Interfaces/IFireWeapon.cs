using UnityEngine;

namespace Interfaces
{
    /// <summary>
    /// Interface for firing weapons
    /// </summary>
    public interface IFireWeapon
    {
        public void DoAttack();
        public void StopAttack();
        public void Fire(Vector3 firePoint, Quaternion rotation);
    }
}