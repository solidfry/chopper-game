using System;
using UnityEngine;

namespace Chopper.Simulation.Loadout
{
    /// <summary>
    /// A built-in named build for one chassis (owner, 28 Nov 2026: "multiple preset builds per chassis"); the hangar's own
    /// saved builds are <c>Presentation.Hangar.LoadoutPreset</c>. One module per chassis slot, in slot order (an empty
    /// entry = an empty slot). Bots fly them (<see cref="Bots.BotClassPick"/>); the hangar can offer them later.
    /// </summary>
    [Serializable]
    public sealed class ChassisPreset
    {
        [Tooltip("Shown to players later (hangar), and in the bot join log.")]
        public string Name = "PRESET";
        [Tooltip("One per chassis slot, in slot order.")]
        public ModuleDefinition[] Modules = Array.Empty<ModuleDefinition>();

        public ChassisPreset() { }

        public ChassisPreset(string name, ModuleDefinition[] modules)
        {
            Name = name;
            Modules = modules ?? Array.Empty<ModuleDefinition>();
        }

        /// <summary>The build as a loadout code for <paramref name="chassis"/>.</summary>
        public LoadoutCode ToCode(ChassisDefinition chassis)
        {
            var ids = new ushort[chassis.Slots.Count];
            for (var i = 0; i < ids.Length; i++)
                ids[i] = Modules != null && i < Modules.Length && Modules[i] != null ? Modules[i].Id : LoadoutSelection.EmptySlot;
            return LoadoutCode.From(new LoadoutSelection(chassis.Id, ids));
        }
    }
}
