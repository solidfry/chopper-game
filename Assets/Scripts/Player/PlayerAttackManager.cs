using Effects.Structs;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;

namespace Player
{
    public class PlayerAttackManager : MonoBehaviour
    {
        [SerializeField] private WeaponSlot[] weaponSlots;
        
        private void Awake() => AssignWeaponSlots();

        public void Fire1(InputAction.CallbackContext ctx) => FireWeapon(weaponSlots[0], ctx);

        public void Fire2(InputAction.CallbackContext ctx) => FireWeapon(weaponSlots[1], ctx);

        public void Fire3(InputAction.CallbackContext ctx)
        {
            // For now this is the main machine gun
            FireWeapon(weaponSlots[2], ctx);
            FireWeapon(weaponSlots[3], ctx);
        }

        private void FireWeapon(WeaponSlot weapon, InputAction.CallbackContext ctx)
        {
            if (ctx.performed)
            {
                weapon.WeaponGameObject.DoAttack();
            }
            else if (ctx.canceled)
            {
                weapon.WeaponGameObject.StopAttack();
            }
        }

        private void AssignWeaponSlots()
        {
            if (weaponSlots == null) return;

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                var weapon = weaponSlots[i].Data.InstantiateWeapon(weaponSlots[i].Transform);
                weaponSlots[i].WeaponGameObject = weapon;
                weapon.name = $"Weapon {i + 1}";
            }
        }
    }
}