using Chopper.Simulation.Input;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chopper.Simulation.Flight
{
    /// <summary>
    /// Reads the Flight action map into the local player's <see cref="ChopperInput"/> each frame.
    /// Keyboard/gamepad axes request a fraction of the max turn rate. The mouse works like aiming: moving it
    /// faster turns faster (degrees per pixel), so it is precise for small corrections and fast for big flicks.
    /// Options come from <see cref="PlayerInputSettings"/>: mouse X steers roll (Battlefield, default) or yaw (aim style),
    /// sensitivity, invert pitch. While a menu is open the input is neutral.
    /// </summary>
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class ChopperInputSystem : SystemBase
    {
        /// <summary>Mouse turn speed at sensitivity 1. Mouse forward = nose down (flight style); invert flips it.</summary>
        private const float MouseDegreesPerPixel = 0.12f;
        // Mouse deltas arrive per frame and are noisy; average them over a few frames.
        private const float MouseSmoothing = 30f;

        private const double CycleRepeatSeconds = 0.2;

        private PlayerControls _controls;
        private double _nextCycleTime;
        private float2 _mouseRate;

        protected override void OnCreate()
        {
            _controls = new PlayerControls();
            _controls.Flight.Enable();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            _controls.Flight.Disable();
            _controls.Dispose();
        }

        protected override void OnUpdate()
        {
            var settings = SystemAPI.TryGetSingleton<PlayerInputSettings>(out var saved) ? saved : PlayerInputSettings.Default;
            if (settings.Blocked)
            {
                _mouseRate = float2.zero;
                foreach (var input in SystemAPI.Query<RefRW<ChopperInput>>().WithAll<GhostOwnerIsLocal>())
                    input.ValueRW = default;
                return;
            }

            var flight = _controls.Flight;
            var thrust = math.clamp(flight.Thrust.ReadValue<float>(), -1f, 1f);
            var yaw = math.clamp(flight.Yaw.ReadValue<float>(), -1f, 1f);
            var pitch = math.clamp(flight.Pitch.ReadValue<float>(), -1f, 1f);
            var roll = math.clamp(flight.Roll.ReadValue<float>(), -1f, 1f);
            var boost = flight.Dash.IsPressed();
            var fire = flight.Fire1.IsPressed();
            var fireSecondary = flight.Fire2.IsPressed();
            var countermeasure = flight.Fire3.WasPressedThisFrame();
            var cyclePylon = CyclePylonPressed();

            // Mouse delta (pixels this frame) -> turn rate in degrees per second, smoothed.
            var dt = math.max(SystemAPI.Time.DeltaTime, 1e-4f);
            var mouseDelta = (float2)flight.MouseLook.ReadValue<Vector2>();
            var targetRate = mouseDelta * (MouseDegreesPerPixel * math.max(settings.MouseSensitivity, 0.05f)) / dt;
            _mouseRate = math.lerp(_mouseRate, targetRate, 1f - math.exp(-MouseSmoothing * dt));

            foreach (var (input, config) in SystemAPI.Query<RefRW<ChopperInput>, RefRO<ChopperFlightConfig>>().WithAll<GhostOwnerIsLocal>())
            {
                var maxRateDegrees = math.degrees(config.ValueRO.MaxTurnRate);
                var mousePitch = (settings.InvertPitch ? -1f : 1f) * _mouseRate.y / maxRateDegrees.x;
                // Battlefield: mouse X rolls. Aim style: mouse X turns the nose and the flight model banks into the turn.
                var aimStyle = settings.MouseSteer == MouseSteer.Yaw;
                var mouseYaw = aimStyle ? _mouseRate.x / maxRateDegrees.y : 0f;
                var mouseRoll = aimStyle ? 0f : _mouseRate.x / maxRateDegrees.z;

                input.ValueRW = new ChopperInput
                {
                    Thrust = thrust,
                    Yaw = math.clamp(yaw + mouseYaw, -1f, 1f),
                    Pitch = math.clamp(pitch + mousePitch, -1f, 1f),
                    Roll = math.clamp(roll + mouseRoll, -1f, 1f),
                    Boost = boost,
                    Fire = fire,
                    FireSecondary = fireSecondary,
                };
                if (countermeasure)
                    input.ValueRW.Countermeasure.Set();
                if (cyclePylon)
                    input.ValueRW.CyclePylon.Set();
            }
        }

        /// <summary>
        /// Mouse wheel, Tab or gamepad D-pad right. Read from the devices for now.
        /// TODO: add a CyclePylon action to PlayerControls.inputactions (needs the editor to regenerate the class).
        /// </summary>
        private bool CyclePylonPressed()
        {
            var now = World.Time.ElapsedTime;
            if (now < _nextCycleTime) return false;

            var wheel = Mouse.current != null && math.abs(Mouse.current.scroll.ReadValue().y) > 0.01f;
            var tab = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
            var dpad = Gamepad.current != null && Gamepad.current.dpad.right.wasPressedThisFrame;
            if (!wheel && !tab && !dpad) return false;

            // One wheel notch can last a few frames: ignore repeats for a moment.
            _nextCycleTime = now + CycleRepeatSeconds;
            return true;
        }
    }
}
