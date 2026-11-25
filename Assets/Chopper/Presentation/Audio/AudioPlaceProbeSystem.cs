using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;

namespace Chopper.Presentation.Audio
{
    /// <summary>
    /// Client only. Measures the world round the listener for the audio (<see cref="IAudioPlace"/>), with rays in the
    /// client's physics world against static geometry only (buildings, ground; not choppers):
    /// <list type="bullet">
    /// <item>Street echo (step 3): <see cref="EnclosureRays"/> level rays round the listener, every
    /// <see cref="EnclosureInterval"/> s. Low between towers = many close hits = more echo, the first echo as late as the
    /// walls are far.</item>
    /// <item>Occlusion (step 4): a ray from the listener to each playing 3D sound, a few per frame in turn
    /// (<see cref="OcclusionRaysPerFrame"/>). A hit = a building in the way: quieter and duller.</item>
    /// </list>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class AudioPlaceProbeSystem : PresentationSystemBase
    {
        public const int EnclosureRays = 8;
        public const float EnclosureRange = 150f;
        public const float EnclosureInterval = 0.1f;
        public const int OcclusionRaysPerFrame = 12;
        /// <summary>The ray stops this far (m) short of the sound, so the sound's own surface does not block it.</summary>
        private const float SourceMargin = 4f;

        private readonly List<AudioEmitter> _emitters = new();
        private readonly List<float> _fractions = new();
        private int _nextEmitter;
        private float _nextEnclosure;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
            RequireForUpdate<PhysicsWorldSingleton>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            var place = context.AudioPlace;
            if (place == null) return;
            CompleteDependency();
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            var listener = (float3)place.Listener;
            var now = UnityEngine.Time.unscaledTime;

            if (now >= _nextEnclosure)
            {
                _nextEnclosure = now + EnclosureInterval;
                ProbeEnclosure(world, listener, place);
            }

            place.Emitters(_emitters);
            if (_emitters.Count == 0) return;
            var count = Mathf.Min(OcclusionRaysPerFrame, _emitters.Count);
            for (var n = 0; n < count; n++)
            {
                _nextEmitter = (_nextEmitter + 1) % _emitters.Count;
                var emitter = _emitters[_nextEmitter];
                place.SetOccluded(emitter.Id, Blocked(world, listener, emitter.Position));
            }
        }

        private void ProbeEnclosure(in CollisionWorld world, float3 listener, IAudioPlace place)
        {
            _fractions.Clear();
            var wallSum = 0f;
            var walls = 0;
            for (var i = 0; i < EnclosureRays; i++)
            {
                var angle = i * (2f * math.PI / EnclosureRays);
                var direction = new float3(math.cos(angle), 0f, math.sin(angle));
                var fraction = StaticHit(world, listener, listener + direction * EnclosureRange);
                _fractions.Add(fraction);
                if (fraction < 0f) continue;
                wallSum += fraction * EnclosureRange;
                walls++;
            }
            place.SetEnclosure(AudioDistance.Enclosure(_fractions), walls > 0 ? wallSum / walls : 0f);
        }

        private static bool Blocked(in CollisionWorld world, float3 listener, float3 source)
        {
            var offset = source - listener;
            var length = math.length(offset);
            if (length <= SourceMargin * 2f) return false;
            var end = listener + offset * ((length - SourceMargin) / length);
            return StaticHit(world, listener, end) >= 0f;
        }

        /// <summary>Fraction (0..1) along the ray of the nearest static hit, or -1 when nothing static is hit.</summary>
        private static float StaticHit(in CollisionWorld world, float3 from, float3 to)
        {
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            try
            {
                var ray = new RaycastInput { Start = from, End = to, Filter = CollisionFilter.Default };
                if (!world.CastRay(ray, ref hits)) return -1f;
                var best = 2f;
                foreach (var hit in hits)
                    if (hit.RigidBodyIndex >= world.NumDynamicBodies && hit.Fraction < best) best = hit.Fraction;
                return best <= 1f ? best : -1f;
            }
            finally
            {
                hits.Dispose();
            }
        }
    }
}
