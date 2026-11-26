using System.Collections.Generic;
using Unity.Burst;
using Chopper.Presentation.Bridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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
            var listener = (float3)place.Listener;
            var now = UnityEngine.Time.unscaledTime;
            var enclosure = now >= _nextEnclosure;
            if (enclosure) _nextEnclosure = now + EnclosureInterval;

            place.Emitters(_emitters);
            var occlusion = Mathf.Min(OcclusionRaysPerFrame, _emitters.Count);
            var rayCount = (enclosure ? EnclosureRays : 0) + occlusion;
            if (rayCount == 0) return;

            // All of this frame's rays in one Burst job (26 Nov: the managed rays cost ~1.3 ms a frame with 9 choppers on City04).
            var rays = new NativeArray<RaycastInput>(rayCount, Allocator.TempJob);
            var fractions = new NativeArray<float>(rayCount, Allocator.TempJob);
            var r = 0;
            if (enclosure)
                for (var i = 0; i < EnclosureRays; i++)
                {
                    var angle = i * (2f * math.PI / EnclosureRays);
                    var direction = new float3(math.cos(angle), 0f, math.sin(angle));
                    rays[r++] = Ray(listener, listener + direction * EnclosureRange);
                }
            var firstEmitter = _nextEmitter;
            for (var n = 0; n < occlusion; n++)
            {
                var index = (firstEmitter + 1 + n) % _emitters.Count;
                var source = (float3)_emitters[index].Position;
                var offset = source - listener;
                var length = math.length(offset);
                // Too close to block: a zero-length ray (no hit).
                rays[r++] = length <= SourceMargin * 2f
                    ? Ray(listener, listener)
                    : Ray(listener, listener + offset * ((length - SourceMargin) / length));
            }

            CompleteDependency();
            var world = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
            new StaticRaysJob { World = world, Rays = rays, Fractions = fractions }.Run();

            r = 0;
            if (enclosure)
            {
                _fractions.Clear();
                var wallSum = 0f;
                var walls = 0;
                for (var i = 0; i < EnclosureRays; i++)
                {
                    var fraction = fractions[r++];
                    _fractions.Add(fraction);
                    if (fraction < 0f) continue;
                    wallSum += fraction * EnclosureRange;
                    walls++;
                }
                place.SetEnclosure(AudioDistance.Enclosure(_fractions), walls > 0 ? wallSum / walls : 0f);
            }
            for (var n = 0; n < occlusion; n++)
            {
                _nextEmitter = (_nextEmitter + 1) % _emitters.Count;
                place.SetOccluded(_emitters[_nextEmitter].Id, fractions[r++] >= 0f);
            }
            rays.Dispose();
            fractions.Dispose();
        }

        private static RaycastInput Ray(float3 from, float3 to) =>
            new() { Start = from, End = to, Filter = CollisionFilter.Default };

        /// <summary>Each ray's fraction (0..1) of the nearest static hit, or -1 when nothing static is hit.</summary>
        [BurstCompile]
        private struct StaticRaysJob : IJob
        {
            [ReadOnly] public CollisionWorld World;
            [ReadOnly] public NativeArray<RaycastInput> Rays;
            public NativeArray<float> Fractions;

            public void Execute()
            {
                for (var i = 0; i < Rays.Length; i++)
                {
                    var ray = Rays[i];
                    if (math.lengthsq(ray.End - ray.Start) < 1e-6f)
                    {
                        Fractions[i] = -1f;
                        continue;
                    }
                    var collector = new ClosestStaticHit(World.NumDynamicBodies);
                    World.CastRay(ray, ref collector);
                    Fractions[i] = collector.NumHits > 0 ? collector.MaxFraction : -1f;
                }
            }
        }

        /// <summary>Keeps the closest hit on a static body (buildings, ground); choppers and wrecks are skipped.</summary>
        private struct ClosestStaticHit : ICollector<Unity.Physics.RaycastHit>
        {
            private readonly int _numDynamic;

            public ClosestStaticHit(int numDynamic)
            {
                _numDynamic = numDynamic;
                MaxFraction = 1f;
                NumHits = 0;
            }

            public bool EarlyOutOnFirstHit => false;
            public float MaxFraction { get; private set; }
            public int NumHits { get; private set; }

            public bool AddHit(Unity.Physics.RaycastHit hit)
            {
                if (hit.RigidBodyIndex < _numDynamic) return false;
                MaxFraction = hit.Fraction;
                NumHits = 1;
                return true;
            }
        }
    }
}
