using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Pure two-bone reach for the dock robot arms (EditMode tests): the turntable yaw, the shoulder pitch and the elbow
    /// bend that put the tool tip on a target, in the arm's own space (y up, the shoulder at the origin).
    /// </summary>
    public static class DockArmReach
    {
        /// <summary>Upper arm length (Prop_DockArm_Upper: shoulder to elbow), m.</summary>
        public const float Upper = 2.4f;
        /// <summary>Forearm and tool (Prop_DockArm_Fore: elbow to tip), m.</summary>
        public const float Fore = 2.45f;

        /// <summary>
        /// Angles in degrees for a target relative to the shoulder: <paramref name="yaw"/> about y (0 = +z),
        /// <paramref name="shoulder"/> about x (positive = down, Unity's x-rotation sense), <paramref name="elbow"/> the
        /// forearm's extra x-rotation from the upper arm. Too far: the arm points straight at it.
        /// </summary>
        public static void Solve(Vector3 target, out float yaw, out float shoulder, out float elbow)
        {
            yaw = Mathf.Atan2(target.x, target.z) * Mathf.Rad2Deg;
            var flat = new Vector2(target.x, target.z).magnitude;
            var distance = Mathf.Clamp(target.magnitude, Mathf.Abs(Upper - Fore) + 0.01f, Upper + Fore - 0.001f);
            var toTarget = Mathf.Atan2(-target.y, flat) * Mathf.Rad2Deg;  // down = positive
            // Law of cosines: the angle at the shoulder between the target line and the upper arm, and at the elbow.
            var atShoulder = Mathf.Acos(Mathf.Clamp((Upper * Upper + distance * distance - Fore * Fore) / (2f * Upper * distance), -1f, 1f)) * Mathf.Rad2Deg;
            var atElbow = Mathf.Acos(Mathf.Clamp((Upper * Upper + Fore * Fore - distance * distance) / (2f * Upper * Fore), -1f, 1f)) * Mathf.Rad2Deg;
            // Elbow up: the upper arm rises above the target line, the forearm bends down to it.
            shoulder = toTarget - atShoulder;
            elbow = 180f - atElbow;
        }

        /// <summary>The tip position for the angles (forward kinematics, for tests and the work light).</summary>
        public static Vector3 Tip(float yaw, float shoulder, float elbow)
        {
            var turn = Quaternion.Euler(0f, yaw, 0f);
            var up = turn * Quaternion.Euler(shoulder, 0f, 0f);
            var fore = up * Quaternion.Euler(elbow, 0f, 0f);
            return up * new Vector3(0f, 0f, Upper) + fore * new Vector3(0f, 0f, Fore);
        }
    }

    /// <summary>
    /// The robot arms and work drones at one base dock (owner, 25 Nov: "a little dock ... little robotic arms and drones
    /// working on it to arm it"). Built from the kit models in <see cref="CombatEffectsSettings"/>; shared by
    /// <see cref="NukeDockSystem"/> and the effects bench. While <see cref="Tick"/> is told the nuke arms here, the arms
    /// reach to points on the nuke and weld (a flickering cyan light), the drones leave their racks and circle it.
    /// </summary>
    public sealed class NukeDockRig : IDisposable
    {
        /// <summary>Arm plinths round the cradle, in dock space (the plinth tops), m.</summary>
        private static readonly Vector3[] Plinths =
        {
            new(-4.5f, 1.6f, -4.5f), new(4.5f, 1.6f, -4.5f), new(-4.5f, 1.6f, 4.5f), new(4.5f, 1.6f, 4.5f),
        };
        /// <summary>Drone charging pads behind the dock, in dock space.</summary>
        private static readonly Vector3[] Racks = { new(-6f, 1.75f, -8.2f), new(6f, 1.75f, -8.2f) };
        /// <summary>Shoulder pivot above the plinth top (Prop_DockArm_Base).</summary>
        private const float ShoulderHeight = 1.1f;
        /// <summary>The nuke body centre above the dock ground (rest height 1.4 minus the hang 0.75).</summary>
        private const float NukeCentre = 0.65f + 1.0f;
        private const float Follow = 2.5f;

        private sealed class Arm
        {
            public Transform Turn, Upper, Fore;
            public Light Weld;
            public Vector3 Shoulder;
            public float Yaw, Pitch, Bend, Phase;
        }

        private sealed class Drone
        {
            public Transform Body;
            public Light Light;
            public Vector3 Rack;
            public float Phase;
        }

        private GameObject _root;
        private readonly Arm[] _arms;
        private readonly Drone[] _drones;
        private float _work;

        public NukeDockRig(CombatEffectsSettings settings, Vector3 dock, Quaternion facing)
        {
            _root = new GameObject("Nuke Dock Rig");
            _root.transform.SetPositionAndRotation(dock, facing);
            _arms = new Arm[Plinths.Length];
            for (var i = 0; i < Plinths.Length; i++)
            {
                var arm = new Arm { Shoulder = Plinths[i] + Vector3.up * ShoulderHeight, Phase = i * 1.7f };
                arm.Turn = Part(settings.DockArmBase, _root.transform, Plinths[i], "Arm Base");
                arm.Upper = Part(settings.DockArmUpper, arm.Turn, Vector3.up * ShoulderHeight, "Arm Upper");
                arm.Fore = Part(settings.DockArmFore, arm.Upper, new Vector3(0f, 0f, DockArmReach.Upper), "Arm Fore");
                arm.Weld = WorkLight(arm.Fore, new Vector3(0f, 0f, DockArmReach.Fore), 4f);
                // Rest: folded up, tool pointing at the cradle.
                Rest(arm);
                _arms[i] = arm;
            }
            _drones = new Drone[Racks.Length];
            for (var i = 0; i < Racks.Length; i++)
            {
                var body = Part(settings.DockDrone, _root.transform, Racks[i], "Work Drone");
                _drones[i] = new Drone { Body = body, Rack = Racks[i], Phase = i * Mathf.PI, Light = WorkLight(body, Vector3.down * 0.3f, 3f) };
            }
        }

        /// <summary>
        /// <paramref name="working"/>: the nuke sits here and arms. <paramref name="progress"/> 0..1 (the drones fly
        /// tighter and the welds come faster as it fills). Unscaled seconds.
        /// </summary>
        public void Tick(bool working, float progress, float time, float deltaTime)
        {
            if (_root == null) return;
            _work = Mathf.MoveTowards(_work, working ? 1f : 0f, deltaTime * 0.8f);
            var blend = 1f - Mathf.Exp(-Follow * deltaTime);
            var centre = new Vector3(0f, NukeCentre, 0f);
            foreach (var arm in _arms)
            {
                // A work point on the nuke's near side that wanders along its body; the arm steps between points.
                var side = new Vector3(Mathf.Sign(arm.Shoulder.x), 0f, 0f);
                var step = Mathf.Floor(time * 0.6f + arm.Phase);
                var along = Mathf.Sin(step * 2.3f + arm.Phase) * 1.2f;
                var target = centre + side * 0.75f + new Vector3(0f, Mathf.Sin(step * 1.7f) * 0.25f, along + Mathf.Sign(arm.Shoulder.z) * 0.3f);
                var rest = arm.Shoulder + new Vector3(-arm.Shoulder.x * 0.25f, 2.6f, -arm.Shoulder.z * 0.25f);
                var goal = Vector3.Lerp(rest, target, _work) - arm.Shoulder;
                DockArmReach.Solve(goal, out var yaw, out var pitch, out var bend);
                arm.Yaw = Mathf.LerpAngle(arm.Yaw, yaw, blend);
                arm.Pitch = Mathf.LerpAngle(arm.Pitch, pitch, blend);
                arm.Bend = Mathf.LerpAngle(arm.Bend, bend, blend);
                Pose(arm);
                if (arm.Weld != null)
                {
                    // Weld flicker while the tool is on the nuke (between steps it lifts off: dark).
                    var onPoint = Mathf.Repeat(time * 0.6f + arm.Phase, 1f) > 0.35f;
                    var flicker = 0.6f + 0.4f * Mathf.PerlinNoise(time * 23f, arm.Phase);
                    arm.Weld.intensity = _work * (onPoint ? 3f * flicker : 0f);
                    arm.Weld.enabled = arm.Weld.intensity > 0.01f;
                }
            }
            foreach (var drone in _drones)
            {
                var radius = Mathf.Lerp(3.2f, 2.2f, progress);
                var angle = time * Mathf.Lerp(0.5f, 0.9f, progress) + drone.Phase;
                var orbit = centre + new Vector3(Mathf.Sin(angle) * radius, 1.6f + Mathf.Sin(time * 1.3f + drone.Phase) * 0.35f, Mathf.Cos(angle) * radius * 1.4f);
                var position = Vector3.Lerp(drone.Rack, orbit, Smooth(_work));
                drone.Body.localPosition = position;
                var look = centre - position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f) drone.Body.localRotation = Quaternion.LookRotation(look) * Quaternion.Euler(8f * _work, 0f, 0f);
                if (drone.Light != null)
                {
                    drone.Light.intensity = _work * (Mathf.PerlinNoise(time * 17f, drone.Phase) > 0.55f ? 2.5f : 0.4f);
                    drone.Light.enabled = drone.Light.intensity > 0.01f;
                }
            }
        }

        public void Dispose()
        {
            if (_root == null) return;
            // The world can shut down after play has ended (domain unload): Destroy is not allowed in edit mode.
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
            _root = null;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static void Rest(Arm arm)
        {
            DockArmReach.Solve(new Vector3(-arm.Shoulder.x * 0.25f, 2.6f, -arm.Shoulder.z * 0.25f), out arm.Yaw, out arm.Pitch, out arm.Bend);
            Pose(arm);
        }

        private static void Pose(Arm arm)
        {
            if (arm.Turn != null) arm.Turn.localRotation = Quaternion.Euler(0f, arm.Yaw, 0f);
            if (arm.Upper != null) arm.Upper.localRotation = Quaternion.Euler(arm.Pitch, 0f, 0f);
            if (arm.Fore != null) arm.Fore.localRotation = Quaternion.Euler(arm.Bend, 0f, 0f);
        }

        /// <summary>The kit model under <paramref name="parent"/>, or an empty pivot when the model is missing.</summary>
        private static Transform Part(GameObject model, Transform parent, Vector3 localPosition, string name)
        {
            var part = model != null ? Object.Instantiate(model, parent, false) : new GameObject(name);
            part.name = name;
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            foreach (var collider in part.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
            return part.transform;
        }

        private static Light WorkLight(Transform parent, Vector3 localPosition, float range)
        {
            var light = new GameObject("Work Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = localPosition;
            light.type = LightType.Point;
            light.color = new Color(0.45f, 0.9f, 1f);
            light.range = range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }
    }
}
