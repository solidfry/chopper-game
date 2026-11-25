using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Pure shape of the nuke cable (EditMode tests): a line from the winch to the nuke's hook eye whose middle hangs
    /// down a little (sag) and bows back against the carrier's flight (the air pushes it). The ends never move.
    /// </summary>
    public static class NukeRopeShape
    {
        /// <summary>The middle's offset from the straight line: down by <paramref name="sag"/>, back against the flat
        /// part of <paramref name="velocity"/> by <paramref name="trail"/> per m/s, at most <paramref name="maxTrail"/>.</summary>
        public static Vector3 Bow(Vector3 velocity, float sag, float trail, float maxTrail)
        {
            var back = -new Vector3(velocity.x, 0f, velocity.z) * trail;
            if (back.sqrMagnitude > maxTrail * maxTrail) back = back.normalized * maxTrail;
            return back + Vector3.down * sag;
        }

        /// <summary>The point at <paramref name="t"/> (0 = winch, 1 = hook eye): full bow in the middle, none at the ends.</summary>
        public static Vector3 Point(Vector3 winch, Vector3 end, Vector3 bow, float t) =>
            Vector3.LerpUnclamped(winch, end, t) + bow * (4f * t * (1f - t));

        /// <summary>
        /// Fills <paramref name="points"/> along the cable. <paramref name="reach"/> (0..1) is how much of it has run out:
        /// 1 = winch to hook eye; less = a line still lowering (the hook bar), its free end part of the way down.
        /// </summary>
        public static void Fill(Vector3 winch, Vector3 end, Vector3 bow, float reach, Vector3[] points)
        {
            reach = Mathf.Clamp01(reach);
            var last = points.Length - 1;
            for (var i = 0; i <= last; i++)
                points[i] = Point(winch, end, bow, last > 0 ? reach * i / last : 0f);
        }
    }

    /// <summary>
    /// One nuke cable on screen (a world-space <see cref="LineRenderer"/>), shared by <see cref="NukeRopeSystem"/> and
    /// the effects bench. It works out the winch's speed from its own movement, so it needs no physics data.
    /// </summary>
    public sealed class NukeRopeView : IDisposable
    {
        /// <summary>A winch that moves further than this in one frame jumped (respawn, round reset): no bow from it.</summary>
        private const float JumpMetres = 30f;
        /// <summary>How fast the bow follows a speed change (1/s): the cable swings into place, not snaps.</summary>
        private const float Follow = 3f;

        private readonly GameObject _root;
        private readonly LineRenderer _line;
        private Vector3[] _points = Array.Empty<Vector3>();
        private Vector3 _lastWinch;
        private Vector3 _velocity;
        private bool _hasLast;

        public NukeRopeView(CombatEffectsSettings settings)
        {
            _root = new GameObject("Nuke Rope");
            _line = _root.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.sharedMaterial = settings.NukeRopeMaterial;
            _line.widthMultiplier = settings.NukeRopeWidth;
            _line.numCapVertices = 0;
            _line.numCornerVertices = 2;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Tile;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _line.receiveShadows = true;
            _line.startColor = _line.endColor = Color.white;
            _line.enabled = false;
        }

        public bool Shown => _line != null && _line.enabled;

        /// <summary>Draws the cable from <paramref name="winch"/> toward <paramref name="end"/>; see <see cref="NukeRopeShape.Fill"/>.</summary>
        public void Draw(CombatEffectsSettings settings, Vector3 winch, Vector3 end, float reach, float deltaTime)
        {
            if (_line == null) return;
            if (_hasLast && deltaTime > 1e-4f)
            {
                var moved = winch - _lastWinch;
                if (moved.sqrMagnitude > JumpMetres * JumpMetres) _velocity = Vector3.zero;
                else _velocity = Vector3.Lerp(_velocity, moved / deltaTime, 1f - Mathf.Exp(-Follow * deltaTime));
            }
            _lastWinch = winch;
            _hasLast = true;

            var count = settings.NukeRopePoints;
            if (_points.Length != count)
            {
                _points = new Vector3[count];
                _line.positionCount = count;
            }
            _line.widthMultiplier = settings.NukeRopeWidth;
            var bow = NukeRopeShape.Bow(_velocity, settings.NukeRopeSag, settings.NukeRopeTrail, settings.NukeRopeMaxTrail);
            NukeRopeShape.Fill(winch, end, bow, reach, _points);
            _line.SetPositions(_points);
            _line.enabled = true;
        }

        public void Hide()
        {
            if (_line != null) _line.enabled = false;
            _hasLast = false;
            _velocity = Vector3.zero;
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
