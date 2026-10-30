using Chopper.Simulation.Input;
using Unity.Entities;
using UnityEngine;

namespace Chopper.Presentation.Bridge
{
    /// <summary>
    /// The player's control options, saved in PlayerPrefs. Set in the options overlay; <see cref="InputSettingsSystem"/>
    /// copies them into simulation.
    /// </summary>
    public sealed class InputOptions
    {
        private const string SteerKey = "Chopper.Input.MouseSteer";
        private const string SensitivityKey = "Chopper.Input.MouseSensitivity";
        private const string InvertKey = "Chopper.Input.InvertPitch";

        private MouseSteer _mouseSteer = (MouseSteer)PlayerPrefs.GetInt(SteerKey, (int)MouseSteer.Roll);
        private float _mouseSensitivity = PlayerPrefs.GetFloat(SensitivityKey, 1f);
        private bool _invertPitch = PlayerPrefs.GetInt(InvertKey, 0) != 0;

        public MouseSteer MouseSteer
        {
            get => _mouseSteer;
            set { _mouseSteer = value; PlayerPrefs.SetInt(SteerKey, (int)value); }
        }

        /// <summary>For UI (which does not reference simulation): 0 = turn (aim), 1 = roll (classic).</summary>
        public int MouseSteerIndex
        {
            get => (int)_mouseSteer;
            set => MouseSteer = (MouseSteer)Mathf.Clamp(value, 0, 1);
        }

        public float MouseSensitivity
        {
            get => _mouseSensitivity;
            set { _mouseSensitivity = Mathf.Clamp(value, 0.1f, 4f); PlayerPrefs.SetFloat(SensitivityKey, _mouseSensitivity); }
        }

        public bool InvertPitch
        {
            get => _invertPitch;
            set { _invertPitch = value; PlayerPrefs.SetInt(InvertKey, value ? 1 : 0); }
        }
    }

    /// <summary>
    /// Client only. Copies the menu state (<see cref="PresentationContext.MenuOpen"/>) and the saved
    /// <see cref="InputOptions"/> into the <see cref="PlayerInputSettings"/> singleton (simulation cannot read presentation).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class InputSettingsSystem : PresentationSystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            EntityManager.CreateSingleton(PlayerInputSettings.Default, "PlayerInputSettings");
        }

        protected override void OnUpdate()
        {
            ref var settings = ref SystemAPI.GetSingletonRW<PlayerInputSettings>().ValueRW;
            if (!TryGetContext(out var context))
            {
                settings.Blocked = false;
                settings.HeadLook = false;
                return;
            }

            var options = context.InputOptions;
            settings.Blocked = context.MenuOpen;
            settings.HeadLook = context.CameraRig != null && context.CameraRig.HeadLookActive;
            settings.MouseSteer = options.MouseSteer;
            settings.MouseSensitivity = options.MouseSensitivity;
            settings.InvertPitch = options.InvertPitch;
        }
    }
}
