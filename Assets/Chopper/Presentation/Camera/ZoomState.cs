using Chopper.Simulation.Combat;
using Unity.Collections;
using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>
    /// The local player's optics (<see cref="OpticsConfig"/>): which zoom step is on, with the smoothed magnification
    /// the camera and the aim speed use, and whether the special view (night, thermal, ...) is on. Local only: optics
    /// change what the player sees, not the simulation. Owned by the presentation context; the Zoom key calls
    /// <see cref="Cycle"/>, the Vision key <see cref="ToggleVision"/>.
    /// </summary>
    public sealed class ZoomState
    {
        /// <summary>How fast the picture moves to a new step (per second, in log space so 1x-2x feels like 4x-8x).</summary>
        private const float Speed = 12f;
        /// <summary>Seconds for the special view to fade in or out.</summary>
        private const float VisionFade = 0.25f;

        private VisionMode _mode;
        private bool _visionOn;
        /// <summary>The mode on screen: kept while the view fades out.</summary>
        private VisionMode _shown;

        /// <summary>The special view of the fitted optics (None = it has none).</summary>
        public VisionMode Mode => _mode;
        public bool VisionOn => _visionOn && _mode != VisionMode.None;
        /// <summary>The mode to draw (also while it fades out), and how much of it, 0..1.</summary>
        public VisionMode ShownVision => VisionBlend > 0f ? _shown : VisionMode.None;
        public float VisionBlend { get; private set; }
        /// <summary>Targets are marked to this distance (0 = no limit).</summary>
        public float VisionRange { get; private set; }

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

        /// <summary>The optics of the chopper flown now. Other optics (a new loadout) switch zoom and view off.</summary>
        public void SetOptics(in OpticsConfig optics)
        {
            VisionRange = optics.VisionRange;
            if (optics.Vision != _mode)
            {
                _mode = optics.Vision;
                _visionOn = false;
                RefreshText();
            }
            SetSteps(optics.Steps);
        }

        /// <summary>The Vision key: the special view on or off. Nothing without one.</summary>
        public void ToggleVision()
        {
            if (_mode == VisionMode.None) return;
            _visionOn = !_visionOn;
            if (_visionOn) _shown = _mode;
            RefreshText();
        }

        /// <summary>HUD name of a special view.</summary>
        public static string VisionName(VisionMode mode) => mode switch
        {
            VisionMode.Night => "NIGHT VISION",
            VisionMode.Thermal => "THERMAL",
            VisionMode.Motion => "KINETIC",
            VisionMode.DeepScan => "DEEP SCAN",
            _ => "",
        };

        /// <summary>The zoom steps alone (an optics module with no special view).</summary>
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
            _mode = VisionMode.None;
            _visionOn = false;
            VisionBlend = 0f;
            VisionRange = 0f;
            SetStep(0);
            RefreshText();
            _logCurrent = 0f;
        }

        /// <summary>The Zoom key: next step, and after the last one back to no zoom.</summary>
        public void Cycle() => SetStep(ZoomMath.Next(Step, _steps.Length));

        private void SetStep(int step)
        {
            if (step == Step) return;
            Step = step;
            RefreshText();
        }

        /// <summary>"THERMAL  ZOOM 4x", one part, or "". Made only on a change (no text work per frame).</summary>
        private void RefreshText()
        {
            var vision = VisionOn ? VisionName(_mode) : "";
            var zoom = Step > 0 ? $"ZOOM {Target:0.#}x" : "";
            Text = vision.Length > 0 && zoom.Length > 0 ? $"{vision}  {zoom}" : vision + zoom;
        }

        public void Tick(float deltaTime)
        {
            var target = Mathf.Log(Mathf.Max(Target, 1f));
            _logCurrent = Mathf.Lerp(_logCurrent, target, 1f - Mathf.Exp(-Speed * Mathf.Max(deltaTime, 0f)));
            if (Mathf.Abs(_logCurrent - target) < 1e-3f) _logCurrent = target;
            VisionBlend = Mathf.MoveTowards(VisionBlend, VisionOn ? 1f : 0f, Mathf.Max(deltaTime, 0f) / VisionFade);
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
