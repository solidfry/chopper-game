using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;

namespace PlayerInteraction
{
    public class PlayerAttackManager : NetworkBehaviour
    {
        [SerializeField] private WeaponSlot[] weaponSlots;
        
        public override void OnNetworkSpawn()
        {
            if (IsClient && IsOwner && !IsHost)
                AssignWeaponSlotsServerRpc();
            if(IsServer || IsHost)
                AssignWeaponSlots();
        }

        public void Fire1(InputAction.CallbackContext ctx) => HandleInput(0, ctx);

        public void Fire2(InputAction.CallbackContext ctx) => HandleInput(1, ctx);

        public void Fire3(InputAction.CallbackContext ctx)
        {
            // For now this is the main machine gun
            HandleInput(2, ctx);
            HandleInput(3, ctx);
        }

        private void HandleInput(int weaponIndex, InputAction.CallbackContext ctx)
        {
            if (weaponIndex < 0 || weaponIndex >= weaponSlots.Length)
            {
                Debug.LogWarning("Invalid weapon index.");
                return;
            }

            if (ctx.performed)
            {
                HandlePerformAttack(weaponIndex);
            }
            else if (ctx.canceled)
            {
                HandleStopAttack(weaponIndex);
            }
        }

        private void HandleStopAttack(int weaponIndex)
        {
            if (IsClient && IsOwner)
                StopAttackServerRpc(weaponIndex);
            else if (IsServer)
                StopAttack(weaponIndex);
        }

        private void HandlePerformAttack(int weaponIndex)
        {
            if (IsClient && IsOwner)
                PerformAttackServerRpc(weaponIndex);
            else if (IsServer)
                PerformAttack(weaponIndex);
        }

        private void PerformAttack(int weaponIndex) => weaponSlots[weaponIndex].DoAttack();

        private void StopAttack(int weaponIndex) => weaponSlots[weaponIndex].StopAttack();

        private void AssignWeaponSlots()
        {
            if (weaponSlots == null) return;

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                var weapon = weaponSlots[i].Data.InstantiateWeapon(weaponSlots[i].Transform, transform);
                weaponSlots[i].WeaponGameObject = weapon;
                if (IsServer)
                {
                    weapon.NetworkObject.Spawn();
                    weapon.NetworkObject.TrySetParent(transform);
                }
                weapon.name = $"Weapon {i + 1}";
                Debug.Log("Weapon slot assigned");
            }
        }
        
        [ServerRpc]
        void PerformAttackServerRpc(int weaponIndex) => PerformAttack(weaponIndex);
        
        [ServerRpc]
        void StopAttackServerRpc(int weaponIndex) => StopAttack(weaponIndex);

        [ServerRpc]
        void AssignWeaponSlotsServerRpc()
        {
            AssignWeaponSlots();
        }
    }
}