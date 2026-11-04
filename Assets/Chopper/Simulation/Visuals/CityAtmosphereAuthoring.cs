using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Chopper.Simulation.Visuals
{
    /// <summary>
    /// The map's height fog (cosmetic): thick on the low ground, thinning out higher up. Baked from the city scene
    /// (Setup step 8 adds it from <c>&lt;kit&gt;.terrain.json</c> "heightFog"); the client's
    /// <c>CityAtmosphereSystem</c> hands it to the full-screen fog pass (<c>Art/Shaders/CityHeightFog.shader</c>).
    /// </summary>
    public struct CityAtmosphere : IComponentData
    {
        /// <summary>Linear colour of the fog.</summary>
        public float3 Colour;
        /// <summary>Fog density at <see cref="BaseHeight"/> (1/m).</summary>
        public float Density;
        /// <summary>World height (m) where the density is <see cref="Density"/>.</summary>
        public float BaseHeight;
        /// <summary>The density drops to 1/e this much higher (m).</summary>
        public float Falloff;
        /// <summary>Most opacity the fog reaches (0..1): far scenery never vanishes completely.</summary>
        public float MaxOpacity;
    }

    [DisallowMultipleComponent]
    public sealed class CityAtmosphereAuthoring : MonoBehaviour
    {
        [SerializeField] private Color colour = new(0.29f, 0.24f, 0.22f);
        [SerializeField, Min(0f), Tooltip("Fog density at the base height (1/m).")] private float density = 0.0016f;
        [SerializeField, Tooltip("World height (m) of the densest fog: the crater or basin floor.")] private float baseHeight;
        [SerializeField, Min(1f), Tooltip("The density drops to 1/e this much higher (m).")] private float falloff = 55f;
        [SerializeField, Range(0f, 1f), Tooltip("Most opacity: far scenery never vanishes completely.")] private float maxOpacity = 0.88f;

        /// <summary>Editor only: used by the setup tool (CityForge import).</summary>
        public void Set(Color fogColour, float fogDensity, float fogBase, float fogFalloff, float fogMax)
        {
            colour = fogColour;
            density = fogDensity;
            baseHeight = fogBase;
            falloff = fogFalloff;
            maxOpacity = fogMax;
        }

        private sealed class Baker : Baker<CityAtmosphereAuthoring>
        {
            public override void Bake(CityAtmosphereAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                var linear = authoring.colour.linear;
                AddComponent(entity, new CityAtmosphere
                {
                    Colour = new float3(linear.r, linear.g, linear.b),
                    Density = authoring.density,
                    BaseHeight = authoring.baseHeight,
                    Falloff = math.max(1f, authoring.falloff),
                    MaxOpacity = math.saturate(authoring.maxOpacity),
                });
            }
        }
    }
}
