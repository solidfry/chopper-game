using Unity.Entities;

namespace Chopper.Simulation.Input
{
    /// <summary>How the mouse's sideways movement steers.</summary>
    public enum MouseSteer : byte
    {
        /// <summary>Aim style: mouse X turns the nose (yaw), the chopper banks by itself. Aim and flight are one.</summary>
        Yaw = 0,
        /// <summary>Classic Battlefield: mouse X rolls; A/D yaw.</summary>
        Roll = 1,
    }

    /// <summary>
    /// Client world singleton: the local player's input options, and whether a menu blocks flight input.
    /// Presentation writes it (<c>InputSettingsSystem</c>, from the saved options); <see cref="Flight.ChopperInputSystem"/> reads it.
    /// </summary>
    public struct PlayerInputSettings : IComponentData
    {
        /// <summary>A menu is open: flight input is neutral.</summary>
        public bool Blocked;
        public MouseSteer MouseSteer;
        /// <summary>Multiplies mouse turn speed. 1 = default.</summary>
        public float MouseSensitivity;
        public bool InvertPitch;

        public static PlayerInputSettings Default => new() { MouseSteer = MouseSteer.Yaw, MouseSensitivity = 1f };
    }
}
