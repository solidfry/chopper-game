﻿using UnityEngine;
using Weapons;

namespace Interfaces
{
    /// <summary>
    /// Interface for objects that can attack, should be instantiated.
    /// </summary>
    public interface IAttackable
    {
        void DoAttack();
        void StopAttack();
        // void InstantiateAttackable(Transform transform, Transform parent);
    }
}