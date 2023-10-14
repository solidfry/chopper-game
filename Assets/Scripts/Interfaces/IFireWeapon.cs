using UnityEngine;

namespace Interfaces
{
    public interface IFireWeapon
    {
        public void DoAttack();
        public void StopAttack();
        public void Fire(Vector3 firePoint, Quaternion rotation);
    }
}