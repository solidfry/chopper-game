using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Camera
{
    /// <summary>A hot or moving thing that is not a lockable target: a rocket in flight, an explosion, a burning flare.</summary>
    public struct HeatMark
    {
        public Vector3 Position;
        /// <summary>Heat as <c>Seeker.Heat</c> counts it (2 = full on thermal).</summary>
        public float Heat;
        /// <summary>m/s, for the kinetic tracker.</summary>
        public float Speed;
    }

    /// <summary>
    /// What the special views mark besides targets. The effect systems that already know these things add them
    /// (rockets each frame, blasts for a short time); <see cref="VisionSystem"/> reads the list once per frame. Owned
    /// by the presentation context. A plain list: nothing is made per frame.
    /// </summary>
    public sealed class HeatMarks
    {
        /// <summary>Most marks kept (a long fight must not grow the lists).</summary>
        public const int Max = 96;

        private struct Timed
        {
            public HeatMark Mark;
            public float Remaining;
            public float Duration;
        }

        private readonly List<HeatMark> _frame = new();
        private readonly List<Timed> _timed = new();

        public int Count => _frame.Count + _timed.Count;

        /// <summary>A thing that moves: add it again each frame while it is there.</summary>
        public void AddFrame(Vector3 position, float heat, float speed)
        {
            if (_frame.Count < Max) _frame.Add(new HeatMark { Position = position, Heat = heat, Speed = speed });
        }

        /// <summary>A thing that stays and cools: a blast. It fades to nothing over <paramref name="seconds"/>.</summary>
        public void AddTimed(Vector3 position, float heat, float seconds)
        {
            if (seconds <= 0f || _timed.Count >= Max) return;
            _timed.Add(new Timed { Mark = new HeatMark { Position = position, Heat = heat }, Remaining = seconds, Duration = seconds });
        }

        /// <summary>Copies the marks of this frame to <paramref name="into"/> (added, not cleared), then forgets the
        /// per-frame ones and cools the timed ones.</summary>
        public void Collect(List<HeatMark> into, float deltaTime)
        {
            into.AddRange(_frame);
            _frame.Clear();
            for (var i = _timed.Count - 1; i >= 0; i--)
            {
                var timed = _timed[i];
                var mark = timed.Mark;
                mark.Heat *= Mathf.Clamp01(timed.Remaining / timed.Duration);
                into.Add(mark);
                timed.Remaining -= deltaTime;
                if (timed.Remaining <= 0f) _timed.RemoveAt(i);
                else _timed[i] = timed;
            }
        }

        public void Clear()
        {
            _frame.Clear();
            _timed.Clear();
        }
    }
}
