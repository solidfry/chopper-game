using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;
using Weapons.ScriptableObjects;

namespace Player
{
    public class PlayerAttackManager : MonoBehaviour
    {
        
        [SerializeField] private WeaponType weaponSlotOne;
        [SerializeField] private WeaponType weaponSlotTwo;
        [SerializeField] private WeaponType weaponSlotThree;
        [SerializeField] private WeaponType weaponSlotFour;
       
        [SerializeField] private Transform weaponSlotOnePosition;
        [SerializeField] private Transform weaponSlotTwoPosition;
        [SerializeField] private Transform weaponSlotThreePosition;
        [SerializeField] private Transform weaponSlotFourPosition;
        
        private Weapon weaponOne;
        private Weapon weaponTwo;
        private Weapon weaponThree;
        private Weapon weaponFour;

        private void Awake()
        {
            if (weaponSlotOne == null && weaponSlotTwo == null) return;
            
            if (weaponSlotOne != null)
            {
                weaponOne = InstantiateWeapon(weaponSlotOne, weaponSlotOnePosition);
                weaponOne.name = "Weapon 1";
            }
            
            if (weaponSlotTwo != null)
            {
                weaponTwo = InstantiateWeapon(weaponSlotTwo, weaponSlotTwoPosition);
                weaponTwo.name = "Weapon 2";
            }
            
            if(weaponSlotThree != null)
            {
                weaponThree = InstantiateWeapon(weaponSlotThree, weaponSlotThreePosition);
                weaponThree.name = "Weapon 3";
            }
            
            if(weaponSlotFour != null)
            {
                weaponFour = InstantiateWeapon(weaponSlotFour, weaponSlotFourPosition);
                weaponFour.name = "Weapon 4";
            }

        }
        
        public void Fire1(InputAction.CallbackContext ctx) => FireWeapon(weaponOne, ctx);

        public void Fire2(InputAction.CallbackContext ctx) => FireWeapon(weaponTwo, ctx);

        public void Fire3(InputAction.CallbackContext ctx)
        {
            FireWeapon(weaponThree, ctx);
            FireWeapon(weaponFour, ctx);
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

        Weapon InstantiateWeapon(WeaponType weaponType, Transform weaponPosition)
        {
            return Instantiate(weaponType.WeaponPrefab, weaponPosition.position, Quaternion.identity, weaponPosition);
        }
    }
}