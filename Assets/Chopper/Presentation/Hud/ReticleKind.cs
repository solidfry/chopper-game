using Chopper.Simulation.Combat;

namespace Chopper.Presentation.Hud
{
    /// <summary>
    /// Centre reticle shape for the selected pylon weapon (owner, 25 Nov: "all different weapon types should have
    /// different reticles"; lock-on missiles most of all). The view adds the USS class <see cref="Reticles.ClassOf"/>.
    /// </summary>
    public enum ReticleKind : byte
    {
        /// <summary>No pylon weapon: the gun ring.</summary>
        Gun = 0,
        /// <summary>Guided missile with a seeker: a wide lock ring.</summary>
        Missile = 1,
        /// <summary>Straight rockets: a square.</summary>
        Rocket = 2,
        /// <summary>Rockets that fall (bombs): an open bracket under the dot.</summary>
        Bomb = 3,
        /// <summary>Rockets that stop and hang (air mines): a small box.</summary>
        Mine = 4,
        /// <summary>Repair drone: a bright green ring.</summary>
        Drone = 5,
    }

    /// <summary>Pure: picks the reticle for a pylon weapon (EditMode tests).</summary>
    public static class Reticles
    {
        public static ReticleKind Of(in PylonWeapon weapon) => weapon.Behaviour switch
        {
            PylonBehaviour.Guided => ReticleKind.Missile,
            PylonBehaviour.Drone => ReticleKind.Drone,
            _ when weapon.Drag > 0f => ReticleKind.Mine,
            _ when weapon.Gravity > 0f => ReticleKind.Bomb,
            _ => ReticleKind.Rocket,
        };

        /// <summary>USS class on the aim element; the gun ring has none.</summary>
        public static string ClassOf(ReticleKind kind) => kind switch
        {
            ReticleKind.Missile => "hud-aim--missile",
            ReticleKind.Rocket => "hud-aim--rocket",
            ReticleKind.Bomb => "hud-aim--bomb",
            ReticleKind.Mine => "hud-aim--mine",
            ReticleKind.Drone => "hud-aim--drone",
            _ => "",
        };
    }
}
