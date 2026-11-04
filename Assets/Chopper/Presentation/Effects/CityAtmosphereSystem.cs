using Chopper.Simulation.Visuals;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Hands the loaded map's height fog (<see cref="CityAtmosphere"/>, baked from the city scene) to the
    /// full-screen fog pass as global shader values; with no city loaded the fog is off (density 0). Cosmetic.
    /// Written without the editor (4 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CityAtmosphereSystem : SystemBase
    {
        private static readonly int FogColour = Shader.PropertyToID("_CityFogColour");
        private static readonly int FogParams = Shader.PropertyToID("_CityFogParams");

        private bool _on;

        protected override void OnUpdate()
        {
            if (SystemAPI.TryGetSingleton<CityAtmosphere>(out var fog))
            {
                Shader.SetGlobalVector(FogColour, new Vector4(fog.Colour.x, fog.Colour.y, fog.Colour.z, 1f));
                Shader.SetGlobalVector(FogParams, new Vector4(fog.Density, fog.BaseHeight, fog.Falloff, fog.MaxOpacity));
                _on = true;
            }
            else if (_on)
            {
                Off();
            }
        }

        protected override void OnDestroy()
        {
            if (_on) Off();
        }

        private void Off()
        {
            Shader.SetGlobalVector(FogParams, Vector4.zero);
            _on = false;
        }
    }
}
