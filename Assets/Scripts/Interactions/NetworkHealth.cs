using System;
using Effects.Structs;
using Events;
using Interfaces;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using Utilities;

namespace Interactions
{
    public class NetworkHealth : NetworkBehaviour, IDamageable
    {
        [SerializeField] int health = 200;
        [field: SerializeField] public int MaxHealth { get; private set; } = 200;
        [SerializeField] public NetworkVariable<int> networkHealth = new (100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        [SerializeField] Death death;
        
        [Header("Environment Damage")]
        [SerializeField] LayerMask environmentLayers;
        [SerializeField] int highSpeedDamageThreshold = 50;
        [SerializeField] float highSpeedDamageMultiplier = 1.5f;
        [SerializeField] int upsideDownDamage = 100;
        [Range(-1,1)]
        [SerializeField] float dotProductEnvironmentAndPlayer = 0;
        
        [Header("Take Damage Events")]
        [SerializeField] CameraShakeEvent takeDamageCameraShake;

        public event Action<int> SendHealthEvent;
        public event Action<ulong> PlayerDiedEvent;
        
        private int _pendingDamage;
        private ulong _lastPlayerToDamage;
        public ulong LastPlayerToDamage => _lastPlayerToDamage;
        
        public override void OnNetworkSpawn()
        {
            // Debug.Log("Player health spawned");
            if(IsClient && IsOwner || IsServer)
            {
                networkHealth.OnValueChanged += OnHealthChanged;
            }
            
            if(IsClient && IsOwner)
            {
                GameEvents.OnPlayerOutOfBoundsDestroyEvent += OutOfBounds;
            }
        }

        public override void OnNetworkDespawn()
        {
            if(IsClient && IsOwner || IsServer)
            {
                networkHealth.OnValueChanged -= OnHealthChanged;
            }
            
            if(IsClient && IsOwner)
            {
                GameEvents.OnPlayerOutOfBoundsDestroyEvent -= OutOfBounds;
            }
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
            
            ClampHealth();
            
        }

        private void ClampHealth()
        {
            if (networkHealth.Value <= 0)
                networkHealth.Value = 0;
        }

        public void TakeDamage(int damageAmount, ulong damagerId)
        {
            if(!IsServer) return;
    
            _pendingDamage += damageAmount;
            _lastPlayerToDamage = damagerId; // Update the last damager ID here
            DoShakeCameraClientRpc(); 
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

        private void OnCollisionEnter(Collision other)
        {
            if(IsClient && !IsOwner) return;
            if (EnvironmentLayersValue(other) != 0)
            {
                CalculateEnvironmentalDamage(other);
            }
        }

        private void CalculateEnvironmentalDamage(Collision other)
        {
            float speed = Speed.MetersPerSecondToKilometersPerHour(other.relativeVelocity.magnitude);
            bool checkDotProduct = Vector3.Dot(transform.up, Vector3.up) < dotProductEnvironmentAndPlayer;
            var addUpsideDownDamage = AddUpsideDownDamage(checkDotProduct);
            var addHighSpeedDamage = AddHighSpeedDamage(speed);
            if(addHighSpeedDamage == 0 && addUpsideDownDamage == 0) return;
            int damage = Mathf.FloorToInt(addHighSpeedDamage + addUpsideDownDamage);
            TakeDamage_ServerRpc(damage, 0);
        }

        private int AddUpsideDownDamage(bool checkDotProduct)
        {
            int addUpsideDownDamage = checkDotProduct ? upsideDownDamage : 0;
            return addUpsideDownDamage;
        }

        private float AddHighSpeedDamage(float speed)
        {
            float addHighSpeedDamage = speed > highSpeedDamageThreshold ? speed * highSpeedDamageMultiplier : 0;
            return addHighSpeedDamage;
        }

        private int EnvironmentLayersValue(Collision other) => environmentLayers.value & (1 << other.transform.gameObject.layer);
        
        private void OutOfBounds(ulong clientid)
        {
            if(clientid != OwnerClientId) return;
            // Debug.Log("Player out of bounds and and was killed");
            TakeDamage_ServerRpc(MaxHealth, 0);
        }
        
        [ServerRpc] 
        void TakeDamage_ServerRpc(int damage, ulong damagerId)
        {
            if(!IsServer) return;
            TakeDamage(damage, damagerId);
        }
        
        [ServerRpc] 
        public void SetPlayerHealthServerRpc(int health)
        {
            if(!IsServer) return;
            networkHealth.Value = health;
            death.SetIsDead(false);
        }
        
        [ServerRpc] 
        void InstantiateDeathParticlesServerRpc()
        {
            if(!IsServer) return;
            var tr = transform;
            var particle = death.InstantiateParticles(tr.position, tr.rotation);
            particle.gameObject.GetComponent<NetworkObject>().Spawn();
        }
        
        [ClientRpc]
        private void DoShakeCameraClientRpc()
        {
            if(!IsOwner) return;
            takeDamageCameraShake.Invoke();
        }
    }
}