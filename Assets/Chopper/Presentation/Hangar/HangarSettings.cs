using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Hangar
{
    /// <summary>
    /// The hangar's look and camera (plan section 5, "Customisation UI"). Materials are made by Setup step 7 from the
    /// style guide palette: Console Green lines on a dark green fill, Cruise Pink for the selected slot, Lightning
    /// Yellow for an empty socket.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Hangar/Hangar Settings", fileName = "HangarSettings")]
    public sealed class HangarSettings : ScriptableObject, IValidatedAsset
    {
        [Header("Online")]
        [SerializeField, Tooltip("Unity Matchmaker queue that FIND MATCH joins (its pool allocates Edgegap servers).")]
        private string matchQueue = "Default";

        [Header("Wireframe")]
        [SerializeField, Tooltip("Line meshes of every chassis and module model (baked by Setup step 7).")]
        private WireframeLibrary wireframes;
        [SerializeField, Tooltip("Solid fill under the lines (hides the far side).")] private Material fill;
        [SerializeField] private Material line;
        [SerializeField, Tooltip("Lines of the module in the selected slot.")] private Material highlight;
        [SerializeField, Tooltip("Marker on an empty socket; it pulses.")] private Material emptySocket;
        [SerializeField, Tooltip("Faces that meet at a bigger angle get a line (used when baking).")]
        private float creaseDegrees = 35f;
        [SerializeField, Tooltip("Metres the lines sit off the surface (used when baking).")]
        private float linePush = 0.01f;

        [Header("Camera")]
        [SerializeField, Tooltip("Overview: camera distance from the chopper's centre, in chopper lengths.")]
        private float overviewDistance = 1.8f;
        [SerializeField, Tooltip("Overview: camera height above the centre, in chopper lengths.")]
        private float overviewHeight = 0.3f;
        [SerializeField] private float overviewFieldOfView = 40f;
        [SerializeField, Tooltip("Turntable speed in the overview (degrees per second).")]
        private float idleSpin = 12f;
        [SerializeField, Tooltip("Slot focus: camera distance from the socket, in chopper lengths. The camera looks at the " +
                                 "socket from the side it sits on, from above.")]
        private float focusDistance = 0.7f;
        [SerializeField, Tooltip("Slot focus: degrees the camera looks down at a socket in the top half of the chopper.")]
        private float focusElevation = 25f;
        [SerializeField, Tooltip("Slot focus: degrees for a socket in the bottom half (below 0 = from below, so wings do " +
                                 "not hide pylons).")]
        private float focusElevationBelow = -12f;
        [SerializeField, Tooltip("Slot focus: field of view.")] private float focusFieldOfView = 35f;
        [SerializeField, Tooltip("Use each socket's own focus pose (SocketAuthoring: offset, field of view, yaw) instead of " +
                                 "the automatic one. For hand-tuned chassis.")]
        private bool useSocketPoses;
        [SerializeField, Tooltip("Seconds to blend the camera and turntable to a slot (about 95 % there).")]
        private float focusBlendSeconds = 0.4f;
        [SerializeField, Tooltip("Empty socket marker: size in metres and pulses per second.")]
        private float socketMarkerSize = 0.5f;
        [SerializeField] private float socketPulseRate = 1.5f;

        [Header("Look")]
        [SerializeField, Tooltip("Bloom and vignette for the neon lines.")] private UnityEngine.Rendering.VolumeProfile volume;

        public WireframeLibrary Wireframes => wireframes;
        public Material Fill => fill;
        public Material Line => line;
        public Material Highlight => highlight;
        public Material EmptySocket => emptySocket;
        public float CreaseDegrees => creaseDegrees;
        public float LinePush => linePush;
        public float OverviewDistance => overviewDistance;
        public float OverviewHeight => overviewHeight;
        public float OverviewFieldOfView => overviewFieldOfView;
        public string MatchQueue => matchQueue;
        public float IdleSpin => idleSpin;
        public float FocusDistance => focusDistance;
        public float FocusElevation => focusElevation;
        public float FocusElevationBelow => focusElevationBelow;
        public float FocusFieldOfView => focusFieldOfView;
        public bool UseSocketPoses => useSocketPoses;
        public float FocusBlendSeconds => focusBlendSeconds;
        public float SocketMarkerSize => socketMarkerSize;
        public float SocketPulseRate => socketPulseRate;
        public UnityEngine.Rendering.VolumeProfile Volume => volume;

        public void CollectProblems(List<string> problems)
        {
            if (wireframes == null) problems.Add($"{name}: no wireframe library. Press '7. Build hangar'.");
            else wireframes.CollectProblems(problems);
            if (fill == null || line == null || highlight == null || emptySocket == null)
                problems.Add($"{name}: a hangar material is missing. Press '7. Build hangar'.");
            if (focusBlendSeconds <= 0f) problems.Add($"{name}: focus blend time must be above 0.");
        }

        /// <summary>Editor only: used by the setup tool.</summary>
        public void Initialise(WireframeLibrary library, Material fillMaterial, Material lineMaterial, Material highlightMaterial,
            Material socketMaterial, UnityEngine.Rendering.VolumeProfile profile)
        {
            wireframes = library;
            fill = fillMaterial;
            line = lineMaterial;
            highlight = highlightMaterial;
            emptySocket = socketMaterial;
            volume = profile;
        }
    }
}
