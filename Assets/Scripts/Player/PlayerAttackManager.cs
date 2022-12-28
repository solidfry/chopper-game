using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;

namespace Player
{
    public class PlayerAttackManager : MonoBehaviour
    {


        
        [SerializeField] private WeaponType weaponSlotOne;
        [SerializeField] private WeaponType weaponSlotTwo;
       
        [SerializeField] private Transform weaponSlotOnePosition;
        [SerializeField] private Transform weaponSlotTwoPosition;

        private Weapon weaponOne;
        private Weapon weaponTwo;

        private void Awake()
        {
            if (weaponSlotOne == null && weaponSlotTwo == null) return;
            
            if (weaponSlotOne != null)
            {
                weaponOne = Instantiate(weaponSlotOne.WeaponPrefab, weaponSlotOnePosition.position, Quaternion.identity, weaponSlotOnePosition).GetComponent<Weapon>();
            }
            
            if (weaponSlotTwo != null)
            {
                weaponTwo = Instantiate(weaponSlotTwo.WeaponPrefab, weaponSlotTwoPosition.position, Quaternion.identity, weaponSlotTwoPosition).GetComponent<Weapon>();
            }
        }
        
        public void Fire1(InputAction.CallbackContext ctx)
        {
            if (ctx.performed)
            {
                // weaponOneIsFiring = true;
                // StartCoroutine(Fire(weaponOneIsFiring));
            }
            else if (ctx.canceled)
            {
                // weaponOneIsFiring = false;
            }
        }
        
        public void Fire2(InputAction.CallbackContext ctx)
        {
            Debug.Log("Fire2");
        }
        //
        // public IEnumerator Fire(bool isFiring, Weapon weaponType)
        // {
        //     while (isFiring)
        //     {
        //         weaponType.DoAttack();
        //         yield return new WaitForSeconds(weaponType.Stats.FireRateInSeconds);
        //     }
        // }
    }
}