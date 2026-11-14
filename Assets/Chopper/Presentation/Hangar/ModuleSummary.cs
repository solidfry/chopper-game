using System.Text;
using Chopper.Presentation.Camera;
using Chopper.Simulation.Combat;
using Chopper.Simulation.Definitions;
using Chopper.Simulation.Loadout;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// The numbers of a weapon or optics module in one short line, for the hangar rows ("100 DPS  600 M  ZOOM 2x").
    /// Read from the definition assets, so a tuned value shows at once. "" for a module with no such data.
    /// </summary>
    public static class ModuleSummary
    {
        private const string Gap = "  ";

        public static string Text(ModuleDefinition module)
        {
            if (module == null) return "";
            if (module.Weapon is HitscanWeaponDefinition gun) return Gun(gun);
            if (module.Weapon is RocketWeaponDefinition rocket) return Rocket(rocket);
            if (module.Weapon is GuidedMissileDefinition missile) return Missile(missile);
            if (module.Ability is OpticsDefinition optics) return Optics(optics);
            return "";
        }

        public static string Gun(HitscanWeaponDefinition gun)
        {
            var text = new StringBuilder();
            text.Append($"{gun.DamagePerSecond:0} DPS").Append(Gap).Append($"{gun.Range:0} M");
            if (gun.FalloffStart > 0f && gun.FalloffStart < gun.Range) text.Append(Gap).Append($"{gun.FalloffFloor * 100f:0}% AT RANGE");
            if (gun.MaxZoom > 1f) text.Append(Gap).Append($"ZOOM TO {gun.MaxZoom:0.#}x");
            return text.ToString();
        }

        public static string Rocket(RocketWeaponDefinition rocket)
        {
            var text = new StringBuilder();
            text.Append($"{rocket.DirectDamage + rocket.SplashDamage:0} DMG").Append(Gap).Append($"{rocket.SplashRadius:0} M BLAST");
            if (rocket.MagazineSize > 0) text.Append(Gap).Append(rocket.SalvoCount > 1 ? $"{rocket.MagazineSize} x {rocket.SalvoCount}" : $"{rocket.MagazineSize} RDS");
            if (rocket.Drag > 0f) text.Append(Gap).Append("MINE");
            else if (rocket.Gravity >= BombGravity) text.Append(Gap).Append("FALLS");
            else text.Append(Gap).Append($"{rocket.Speed:0} M/S");
            if (rocket.ProximityFuse > 0f) text.Append(Gap).Append($"FUSE {rocket.ProximityFuse:0} M");
            return text.ToString();
        }

        public static string Missile(GuidedMissileDefinition missile)
        {
            var text = new StringBuilder();
            text.Append($"{missile.DirectDamage + missile.SplashDamage:0} DMG").Append(Gap).Append($"LOCK {missile.LockRange:0} M");
            if (missile.MagazineSize > 0) text.Append(Gap).Append(missile.SalvoCount > 1 ? $"{missile.MagazineSize} x {missile.SalvoCount}" : $"{missile.MagazineSize} RDS");
            return text.ToString();
        }

        public static string Optics(OpticsDefinition optics)
        {
            var text = new StringBuilder();
            var levels = optics.ZoomLevels;
            for (var i = 0; levels != null && i < levels.Count && i < ZoomMath.MaxSteps; i++)
                text.Append(i == 0 ? "ZOOM " : " / ").Append($"{levels[i]:0.#}x");
            if (optics.Vision != VisionMode.None)
            {
                if (text.Length > 0) text.Append(Gap);
                text.Append(ZoomState.VisionName(optics.Vision));
                if (optics.VisionRange > 0f) text.Append($" {optics.VisionRange:0} M");
            }
            return text.ToString();
        }

        /// <summary>A rocket that falls at least this fast (m/s2) is a bomb: its launch speed says nothing.</summary>
        private const float BombGravity = 10f;
    }
}
