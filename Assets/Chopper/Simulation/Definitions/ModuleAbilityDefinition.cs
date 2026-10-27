using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Simulation.Definitions
{
    /// <summary>
    /// Base of non-weapon module behaviours (utility slot): flares now; later chaff, smoke, EMP, shield, drones.
    /// A module points to one; the chopper baker turns it into ECS components.
    /// </summary>
    public abstract class ModuleAbilityDefinition : ScriptableObject, IValidatedAsset
    {
        public virtual void CollectProblems(List<string> problems)
        {
        }
    }
}
