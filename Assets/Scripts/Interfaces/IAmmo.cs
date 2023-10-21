using Weapons.ScriptableObjects;

namespace Interfaces
{
    public interface IAmmo
    {
        void SetAmmoType(AmmoType ammoTypeToSet);
        void SetMaxRange(float maxRange);
        AmmoType GetAmmoType();
    }
}