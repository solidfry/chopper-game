using UnityEngine;
using UnityEngine.U2D;

namespace Interfaces
{
    public interface IFireWeapon
    {
        public void DoAttack();
        public void StopAttack();
        public void Fire(Transform firePoint, float speed, float damage, float range, float spread = 0);
    }
}