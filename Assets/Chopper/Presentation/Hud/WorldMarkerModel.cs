using System;
using UnityEngine;

namespace Chopper.Presentation.Hud
{
    public enum WorldMarkerKind : byte
    {
        /// <summary>Another player's chopper.</summary>
        Enemy = 0,
        /// <summary>Ground target (training targets now; later turrets, vehicles).</summary>
        Ground = 1,
    }

    /// <summary>One screen marker. Positions are viewport space: (0,0) bottom left to (1,1) top right.</summary>
    public struct WorldMarker
    {
        public WorldMarkerKind Kind;
        /// <summary>On screen: the target's place. Off screen: clamped to the screen edge, towards the target.</summary>
        public Vector2 Viewport;
        public bool OnScreen;
        /// <summary>Off screen only: arrow direction, clockwise from up (0 = up, 90 = right).</summary>
        public float EdgeAngleDegrees;
        /// <summary>Metres, rounded to <see cref="WorldMarkerModel.DistanceStep"/>.</summary>
        public int Distance;
        /// <summary>0 to 1; below 0 = no bar (too far to read).</summary>
        public float HealthFraction;
        /// <summary>The selected seeker is locking or has locked this target.</summary>
        public bool IsLockTarget;
    }

    /// <summary>
    /// View model for world markers (plan section 3b, "Dynamic HUD overlay") and the rocket lead indicator.
    /// <see cref="WorldMarkerSystem"/> fills it each frame (<see cref="Begin"/>, <see cref="Add"/>, <see cref="End"/>);
    /// the HUD view draws it with a fixed pool of elements. Markers change every frame, so this raises one
    /// <see cref="Changed"/> event per frame instead of per-property events.
    /// </summary>
    public sealed class WorldMarkerModel
    {
        /// <summary>Most markers shown at once (the closest ones).</summary>
        public const int Capacity = 24;
        public const int DistanceStep = 10;
        /// <summary>Off-screen arrows stay this far (viewport) from the screen edge.</summary>
        public const float EdgeMargin = 0.04f;

        private readonly WorldMarker[] _markers = new WorldMarker[Capacity];

        public event Action Changed;

        public int Count { get; private set; }
        public ReadOnlySpan<WorldMarker> Markers => new(_markers, 0, Count);

        /// <summary>Where to put the nose so the selected rockets hit the target.</summary>
        public bool LeadVisible { get; private set; }
        public Vector2 LeadViewport { get; private set; }

        public void Begin()
        {
            Count = 0;
            LeadVisible = false;
        }

        /// <summary>False when full.</summary>
        public bool Add(in WorldMarker marker)
        {
            if (Count >= Capacity) return false;
            _markers[Count++] = marker;
            return true;
        }

        /// <param name="viewport">Lead point in viewport space; z below 0 = behind the camera (hidden).</param>
        public void SetLead(Vector3 viewport)
        {
            LeadVisible = viewport.z > 0f && viewport.x is >= 0f and <= 1f && viewport.y is >= 0f and <= 1f;
            if (LeadVisible) LeadViewport = new Vector2(viewport.x, viewport.y);
        }

        public void End() => Changed?.Invoke();

        /// <summary>No local chopper: nothing to show.</summary>
        public void Clear()
        {
            if (Count == 0 && !LeadVisible) return;
            Begin();
            End();
        }

        /// <summary>
        /// Places a marker from a camera viewport point (x, y, z = depth). On screen: the point itself. Off screen or
        /// behind the camera: on the screen edge in the target's direction, with the arrow angle.
        /// </summary>
        public static bool Place(Vector3 viewport, out Vector2 position, out float edgeAngleDegrees)
        {
            var behind = viewport.z <= 0f;
            if (!behind && viewport.x is >= 0f and <= 1f && viewport.y is >= 0f and <= 1f)
            {
                position = new Vector2(viewport.x, viewport.y);
                edgeAngleDegrees = 0f;
                return true;
            }

            var direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            // Behind the camera the projection is mirrored.
            if (behind) direction = -direction;
            if (direction.sqrMagnitude < 1e-8f) direction = Vector2.down;

            var half = 0.5f - EdgeMargin;
            var scale = half / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.y));
            position = new Vector2(0.5f, 0.5f) + direction * scale;
            edgeAngleDegrees = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            return false;
        }
    }
}
