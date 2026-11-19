using System;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// One layer of a layered explosion (<see cref="CombatEffectsSettings.ChopperExplosionLayers"/>): a particle prefab
    /// (it may loop: it emits for <see cref="EmitSeconds"/> only), scaled, started after <see cref="Delay"/>, and
    /// removed after <see cref="LifeSeconds"/> so its last particles can fade. It can grow from
    /// <see cref="StartScale"/> (new particles come out bigger and faster as it swells) and sit off the point by
    /// <see cref="Spread"/>.
    /// </summary>
    [Serializable]
    public struct BlastLayer
    {
        public GameObject Prefab;
        [Min(0.01f)] public float Scale;
        [Min(0f)] public float Delay;
        [Tooltip("Seconds it emits; 0 = as the prefab is made (a one-shot).")]
        [Min(0f)] public float EmitSeconds;
        [Min(0.1f)] public float LifeSeconds;
        [Tooltip("Metres above the blast point.")]
        public float Up;
        [Tooltip("Scale at the start; it grows to Scale over Grow Seconds (a blast that swells, not one that pops in). " +
                 "0 = starts at Scale.")]
        [Min(0f)] public float StartScale;
        [Min(0f)] public float GrowSeconds;
        [Tooltip("Random offset round the blast point (m, mostly sideways): secondary blasts scatter.")]
        [Min(0f)] public float Spread;
        [Tooltip("Material for all its particle renderers (a recoloured copy of the pack's). Empty = the prefab's own.")]
        public Material Material;
        [Tooltip("Peak colour over each particle's life (black, this, black). Alpha 0 = the prefab's own colours.")]
        public Color LifeColour;
        [Tooltip("Burns on the ground (lasting flames, the smoke column). In a blast in the air it starts where the falling " +
                 "wreck lands, not in the air; no ground under the fall = not shown.")]
        public bool Ground;
    }
}
