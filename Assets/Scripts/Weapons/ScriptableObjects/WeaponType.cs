﻿using System.Collections.Generic;
using Effects.Structs;
using UnityEngine;

namespace Weapons.ScriptableObjects
{
    [CreateAssetMenu(fileName = "_WeaponType", menuName = "Weapons/New Weapon", order = 0)]
    public class WeaponType : ScriptableObject
    {
        [SerializeField] private AmmoType ammoType;
        [field: SerializeField] public float FirePointOffset { get; private set; } = 2f;
        [SerializeField] private WeaponStats stats;
        [SerializeField] private Weapon weaponPrefab;
        [SerializeField] public CameraShakeEvent shakeEvent;
        [SerializeField] private GameObject weaponMesh;
        [SerializeField] List<AudioClip> weaponFireClips;
        WeaponType _weaponType => this;
        
    
        public AmmoType AmmoType
        {
            get => ammoType;
            private set => ammoType = value;
        }
        public WeaponStats Stats
        {
            get => stats;
            private set => stats = value;
        }

        public Weapon WeaponPrefab
        {
            get => weaponPrefab;
            private set => weaponPrefab = value;
        }

        public GameObject WeaponMesh
        {
            get => weaponMesh;
            private set => weaponMesh = value;
        }

        public AmmoEffect InstantiateAmmoFromWeapon(Vector3 position, Quaternion rotation)
        {
            // Debug.Log($"Firing weapon {name}");
            Vector3 forward = rotation * Vector3.forward;
            position += forward * FirePointOffset;
            AmmoEffect projectile = AmmoType.InstantiateAmmo(position, rotation);
            return projectile;
        }
        
        AudioClip GetRandomFireClip() => weaponFireClips[Random.Range(0, weaponFireClips.Count)];
        
        public Weapon InstantiateWeapon(Transform weaponPosition, Transform parent)
        {
            Weapon weapon = Instantiate(weaponPrefab, weaponPosition.position, weaponPosition.rotation, parent);
            Debug.Log("Instance of weapon created");
            weapon.SetWeaponType(_weaponType);
            weapon.SetStats(Stats);
            weapon.SetAmmoType(AmmoType);
            return weapon;
        }
    }
}