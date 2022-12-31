using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;
using Weapons.ScriptableObjects;

namespace Player
{
    public class PlayerAttackManager : MonoBehaviour
    {
        // All of these should be arrays probably
        [SerializeField] private WeaponType[] weaponSlots;
        [SerializeField] private Transform[] weaponSlotPositions;
        
        private List<Weapon> weapons;

        private void Awake()
        {
            if (weaponSlots == null || weaponSlotPositions == null || weaponSlots.Length == 0 || weaponSlotPositions.Length == 0) return;

            AssignWeaponSlots();
        }

        public void Fire1(InputAction.CallbackContext ctx) => FireWeapon(weapons[0], ctx);

        public void Fire2(InputAction.CallbackContext ctx) => FireWeapon(weapons[1], ctx);

        public void Fire3(InputAction.CallbackContext ctx)
        {
            // For now this is the main machine gun
            FireWeapon(weapons[2], ctx);
            FireWeapon(weapons[3], ctx);
        }

        private void FireWeapon(Weapon weapon, InputAction.CallbackContext ctx)
        {
            if (ctx.performed)
            {
                weapon.DoAttack();
            }
            else if (ctx.canceled)
            {
                weapon.StopAttack();
            }
        }
        
        private void AssignWeaponSlots()
        {
            weapons = new List<Weapon>();
            for (int i = 0; i < weaponSlots.Length; i++)
            {
                if (weaponSlots[i] == null) continue;
                
                var weapon = weaponSlots[i].InstantiateWeapon(weaponSlotPositions[i]);
                weapon.name = $"Weapon {i + 1}";
                weapons.Add(weapon);
            }
        }
    }
}