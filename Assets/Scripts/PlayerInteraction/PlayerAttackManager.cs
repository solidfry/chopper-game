﻿using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using Weapons;

namespace PlayerInteraction
{
    public class PlayerAttackManager : NetworkBehaviour
    {
        [SerializeField] private WeaponSlot[] weaponSlots;
        List<Weapon> _weapons = new ();
        private bool _weaponsAssigned = false;

        public override void OnNetworkSpawn()
        {
            if(IsClient && IsOwner && IsLocalPlayer)
            {
                AssignWeaponSlotsServerRpc();
            }
            
            if (IsServer)
            {
                AssignWeaponSlots();
            }
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
            if (_weaponsAssigned || weaponSlots == null) return;

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                var weapon = weaponSlots[i].WeaponType.InstantiateWeapon(weaponSlots[i].Transform, transform);
                weaponSlots[i].WeaponGameObject = weapon;
                weapon.name = $"Weapon {i + 1}";
                weapon.Initialise(OwnerClientId, weaponSlots[i].Transform);
                _weapons.Add(weapon);
            }
            _weaponsAssigned = true;
        } 
        
        [ServerRpc]
        private void AssignWeaponSlotsServerRpc()
        {
            if (_weaponsAssigned || weaponSlots == null) return;

            for (int i = 0; i < weaponSlots.Length; i++)
            {
                var weapon = weaponSlots[i].WeaponType.InstantiateWeapon(weaponSlots[i].Transform, transform);
                weaponSlots[i].WeaponGameObject = weapon;
                weapon.name = $"Weapon {i + 1}";
                // weapon.Initialise(OwnerClientId, weaponSlots[i].Transform);
                weapon.NetworkObject.SpawnWithOwnership(OwnerClientId);
                weapon.NetworkObject.TrySetParent(transform);
                _weapons.Add(weapon);
            }
            _weaponsAssigned = true;
        } 
  
        [ServerRpc]
        void PerformAttackServerRpc(int weaponIndex) => PerformAttack(weaponIndex);
        
        [ServerRpc]
        void StopAttackServerRpc(int weaponIndex) => StopAttack(weaponIndex);
        
    }
}