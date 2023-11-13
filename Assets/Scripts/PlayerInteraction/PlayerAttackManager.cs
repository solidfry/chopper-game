using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using Weapons;

namespace PlayerInteraction
{
    public class PlayerAttackManager : NetworkBehaviour
    {
        [FormerlySerializedAs("layerMask")] [SerializeField] LayerMask ignoreCollisionMask;
        [SerializeField] private WeaponSlot[] weaponSlots;
        
        public void Start()
        {
            foreach (var weapon in weaponSlots)
            {
                if(IsServer && !IsLocalPlayer)
                    weapon.OnAttack += FireServer;
                if (IsLocalPlayer && IsClient)
                    weapon.OnAttack += FireClient;
            }
        }
        
        public override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var weapon in weaponSlots)
            {
                if(IsServer && !IsLocalPlayer)
                    weapon.OnAttack -= FireServer;
                if(IsLocalPlayer && IsClient)
                    weapon.OnAttack -= FireClient;
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
            {
                if(IsLocalPlayer) StopAttack(weaponIndex);
                StopAttackServerRpc(weaponIndex);
            }
            else if (IsServer)
                StopAttack(weaponIndex);
        }

        private void HandlePerformAttack(int weaponIndex)
        {
            if (IsClient && IsOwner)
            {
                if(IsLocalPlayer) PerformAttack(weaponIndex);
                PerformAttackServerRpc(weaponIndex);
            }            
            else if (IsServer)
                PerformAttack(weaponIndex);
        }

        private void PerformAttack(int weaponIndex)
        {
            var weapon = weaponSlots[weaponIndex];
            if (weapon.weaponGameObjectInstance.firingCooldownTimer <= 0)
            {
                weapon.DoAttack();
            }
        }
        private void StopAttack(int weaponIndex) => weaponSlots[weaponIndex].StopAttack();

        private void FireServer(WeaponSlot weapon, Vector3 position, Quaternion rotation)
        {
            if (!IsServer) return;
            var speed = weapon.weaponGameObjectInstance.stats.ProjectileSpeed;
            var projectile = weapon.Fire(position, rotation);
            var forward = rotation * Vector3.forward;
            projectile.GetComponent<NetworkObject>().SpawnWithOwnership(OwnerClientId);
            var goServer = projectile.gameObject;
            HandleProjectileRb(goServer, forward, speed);
        }
        
        private void FireClient(WeaponSlot weapon, Vector3 position, Quaternion rotation)
        {
            if (!IsLocalPlayer && !IsClient) return;
            var speed = weapon.weaponGameObjectInstance.stats.ProjectileSpeed;
            var projectileClient = weapon.FireClient(position, rotation);
            var forward = rotation * Vector3.forward;
            var goClient = projectileClient.gameObject;
            HandleProjectileRb(goClient, forward, speed);
        }

        private void HandleProjectileRb(GameObject projectile, Vector3 forward, float speed)
        {
            var rb = projectile.GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.isKinematic = false;
            rb.excludeLayers = ignoreCollisionMask;
            rb.GetComponent<Collider>().excludeLayers = ignoreCollisionMask;
            rb.AddForce(forward * speed,
                ForceMode.VelocityChange);
        }
        
        [ServerRpc]
        void PerformAttackServerRpc(int weaponIndex) => PerformAttack(weaponIndex);
        
        [ServerRpc]
        void StopAttackServerRpc(int weaponIndex) => StopAttack(weaponIndex);
        
    }
}