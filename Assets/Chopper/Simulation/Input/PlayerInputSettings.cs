using Unity.Entities;

namespace Chopper.Simulation.Input
{
    /// <summary>How the mouse's sideways movement steers.</summary>
    public enum MouseSteer : byte
    {
        /// <summary>Aim style: mouse X turns the nose (yaw), the chopper banks by itself.</summary>
        Yaw = 0,
        /// <summary>Battlefield (default): mouse X rolls; A/D yaw.</summary>
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
        /// <summary>Head look (Alt / R3) is held: mouse and pitch / roll axes are neutral, the camera uses them.</summary>
        public bool HeadLook;
        public MouseSteer MouseSteer;
        /// <summary>Multiplies mouse turn speed. 1 = default.</summary>
        public float MouseSensitivity;
        public bool InvertPitch;
        /// <summary>Zoom: multiplies the mouse turn speed (<see cref="Combat.ZoomMath.MouseScale"/>). 0 = 1 (not set).</summary>
        public float ZoomMouseScale;
        /// <summary>Zoom: multiplies the pitch, roll and yaw axes (<see cref="Combat.ZoomMath.AxisScale"/>). 0 = 1 (not set).</summary>
        public float ZoomAxisScale;

        public static PlayerInputSettings Default => new() { MouseSteer = MouseSteer.Roll, MouseSensitivity = 1f };
    }
}
