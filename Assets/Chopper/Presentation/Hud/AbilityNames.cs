using Chopper.Simulation.Combat;

namespace Chopper.Presentation.Hud
{
    /// <summary>HUD names of the pilot abilities (the select screen shows the pilot asset's own name and text).</summary>
    public static class AbilityNames
    {
        public static string Of(AbilityKind kind) => kind switch
        {
            AbilityKind.NaniteCloud => "NANITE CLOUD",
            AbilityKind.VenomBelt => "VENOM BELT",
            AbilityKind.NapalmSalvo => "NAPALM SALVO",
            AbilityKind.Ghost => "GHOST",
            AbilityKind.Aegis => "AEGIS",
            AbilityKind.Overclock => "OVERCLOCK",
            AbilityKind.Ping => "PING",
            AbilityKind.Blackout => "BLACKOUT",
            AbilityKind.StripDown => "STRIP DOWN",
            AbilityKind.BatteringRam => "BATTERING RAM",
            _ => "",
        };
    }
}
