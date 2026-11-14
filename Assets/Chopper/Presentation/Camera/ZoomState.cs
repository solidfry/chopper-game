using Chopper.Simulation.Combat;
using Unity.Collections;
using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// The local player's zoom: which step of the chopper's optics is on (<see cref="OpticsConfig"/>), and the smoothed
    /// magnification the camera and the aim speed use. Local only: zoom changes what the player sees, not the
    /// simulation. Owned by the presentation context; the Zoom key calls <see cref="Cycle"/>.
    /// </summary>
    public sealed class ZoomState
    {
        /// <summary>How fast the picture moves to a new step (per second, in log space so 1x-2x feels like 4x-8x).</summary>
        private const float Speed = 12f;

        private FixedList32Bytes<float> _steps;
        private float _logCurrent;

        /// <summary>0 = no zoom, 1.. = a step of the optics.</summary>
        public int Step { get; private set; }
        public int StepCount => _steps.Length;
        /// <summary>The magnification of the chosen step (1 = no zoom).</summary>
        public float Target => ZoomMath.Magnification(_steps, Step);
        /// <summary>The magnification on screen now (it glides to <see cref="Target"/>).</summary>
        public float Current => Mathf.Exp(_logCurrent);
        public bool Zoomed => Step > 0;
        /// <summary>HUD text: "ZOOM 4x" while zoomed, "" without.</summary>
        public string Text { get; private set; } = "";

        /// <summary>The steps of the chopper flown now. Other steps (a new loadout) switch the zoom off.</summary>
        public void SetSteps(in FixedList32Bytes<float> steps)
        {
            if (Same(_steps, steps)) return;
            _steps = steps;
            SetStep(0);
        }

        /// <summary>No chopper (dead, not spawned): zoom off at once.</summary>
        public void Clear()
        {
            _steps = default;
            SetStep(0);
            _logCurrent = 0f;
        }

        /// <summary>The Zoom key: next step, and after the last one back to no zoom.</summary>
        public void Cycle() => SetStep(ZoomMath.Next(Step, _steps.Length));

        private void SetStep(int step)
        {
            if (step == Step && (step > 0) == (Text.Length > 0)) return;
            Step = step;
            Text = step > 0 ? $"ZOOM {Target:0.#}x" : "";
        }

        public void Tick(float deltaTime)
        {
            var target = Mathf.Log(Mathf.Max(Target, 1f));
            _logCurrent = Mathf.Lerp(_logCurrent, target, 1f - Mathf.Exp(-Speed * Mathf.Max(deltaTime, 0f)));
            if (Mathf.Abs(_logCurrent - target) < 1e-3f) _logCurrent = target;
        }

        private static bool Same(in FixedList32Bytes<float> a, in FixedList32Bytes<float> b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (!Mathf.Approximately(a[i], b[i])) return false;
            return true;
        }
    }
}
