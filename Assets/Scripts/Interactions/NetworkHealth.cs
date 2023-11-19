using System;
using Events;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using Utilities;

namespace Interactions
{
    public class NetworkHealth : NetworkBehaviour, IDamageable
    {
        [SerializeField] int health = 200;
        [field: SerializeField] public int MaxHealth { get; private set; } = 200;
        [SerializeField] public NetworkVariable<int> networkHealth = new (100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        [SerializeField] Death death;
        public event Action<int> SendHealthEvent;
        public event Action<ulong> PlayerDiedEvent;
        
        private int _pendingDamage;
        private ulong _lastPlayerToDamage;
        public ulong LastPlayerToDamage => _lastPlayerToDamage;
        
        public override void OnNetworkSpawn()
        {
            Debug.Log("Player health spawned");
            if(IsClient && IsOwner || IsServer)
            {
                networkHealth.OnValueChanged += OnHealthChanged;
            }
        }
        
        public override void OnNetworkDespawn()
        {
            if(IsClient && IsOwner || IsServer)
                networkHealth.OnValueChanged -= OnHealthChanged;
        }

        private void FixedUpdate()
        {
            if(!IsServer) return;

            if (_pendingDamage <= 0) return;
            
            ApplyDamage(_pendingDamage);
            _pendingDamage = 0;
        }

        private void ApplyDamage(int totalDamage)
        {
            if(!IsServer) return;
            
            if(networkHealth.Value <= 0) return;
            
            networkHealth.Value -= totalDamage;
            // Debug.Log(totalDamage + " damage taken" + " health is now " + networkHealth.Value);
        }

        public void TakeDamage(int damageAmount, ulong damagerId)
        {
            if(!IsServer) return;
    
            _pendingDamage += damageAmount;
            _lastPlayerToDamage = damagerId; // Update the last damager ID here
        }
  
        private void OnHealthChanged(int previousvalue, int newvalue)
        {
            health = newvalue;
            Debug.Log("Health changed to " + newvalue);
            SendHealthEvent?.Invoke(health);
            if (newvalue <= 0)
            {
                Die();
            }
        }

        public void Die()
        {
            if(IsOwner && IsClient)
                InstantiateDeathParticlesServerRpc();
            
            if(!IsServer) return;
    
            if (!death.IsDead)
            {
                // Before setting death state, handle the last damage attribution

                death.SetIsDead(true);
                Debug.Log($"Player died. Killed by player {_lastPlayerToDamage}");
                GameEvents.OnPlayerDiedEvent?.Invoke(OwnerClientId); // You might want to pass the _lastDamagerId as well
                PlayerDiedEvent?.Invoke(OwnerClientId);
                
                if(LastPlayerToDamage != 0)
                    GameEvents.OnPlayerKillEvent?.Invoke(_lastPlayerToDamage);
                
                // Reset last damager ID after handling death
                _lastPlayerToDamage = 0;
            }
        }

        public void InitialiseHealth()
        {
            if(IsClient && IsOwner)
            {
                SetPlayerHealthServerRpc(health);
            }
            
            Debug.Log("Health initialised for " + OwnerClientId);
        }
        
        [ServerRpc] 
        public void SetPlayerHealthServerRpc(int health)
        {
            if(!IsServer) return;
            networkHealth.Value = health;
            death.SetIsDead(false);
        }
        
        [ServerRpc] 
        public void InstantiateDeathParticlesServerRpc()
        {
            if(!IsServer) return;
            var particle = death.InstantiateParticles();
            particle.gameObject.GetComponent<NetworkObject>().Spawn();
        }

        private void OnCollisionEnter(Collision other)
        {
            if (!IsServer) return;

            // Check for upside-down collision
            if (other.gameObject.layer == LayerMask.NameToLayer("Environment") && Vector3.Dot(transform.up, Vector3.down) > 0)
            {
                TakeDamage(100, 0);
                Debug.Log("Hit your roof!");
                return;
            }
    
            // Check for high-speed collision
            if (other.gameObject.layer == LayerMask.NameToLayer("Environment") && Speed.MetersPerSecondToKilometersPerHour(GetComponent<Rigidbody>().velocity.magnitude) > 50)
            {
                TakeDamage(200, 0);
                Debug.Log("Hit too hard!");
                return;
            }
        }
    }
    
}