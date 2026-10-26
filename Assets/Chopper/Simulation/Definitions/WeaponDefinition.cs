using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>
    /// Base of all weapon tuning assets. A weapon module (<see cref="Loadout.ModuleDefinition"/>) points to one;
    /// the chopper baker turns it into ECS components.
    /// </summary>
    public abstract class WeaponDefinition : ScriptableObject, IValidatedAsset
    {
        [SerializeField, Tooltip("Gun or pylon position in chopper space (z = forward). TODO: take it from the chassis socket.")]
        private Vector3 muzzleOffset = new(0f, -0.6f, 2.5f);

        public Vector3 MuzzleOffset => muzzleOffset;

        public virtual void CollectProblems(List<string> problems)
        {
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void SetMuzzleOffset(Vector3 offset) => muzzleOffset = offset;
    }
}
