using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Simulation.Combat;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Each time a chopper drops flares (<see cref="CountermeasureState.FlareCount"/> goes up) they fire out
    /// one by one, left and right in turn (in time with the pops of the flare sound), then hang, fizz and burn out in the
    /// sky. Cosmetic: the server decides what the flares do.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class FlareEffectsSystem : PresentationSystemBase
    {
        // Same flight as the server's flare cloud, so the lock box and missiles sit on the flares.
        private const float FlareGravity = FlareFlight.Gravity;
        private const float FlareDrag = FlareFlight.Drag;
        private const float FizzRate = 22f;

        private sealed class Flare
        {
            public GameObject Visual;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Remaining;
            public float Lifetime;
            public Vector3 BaseScale;
            public float Seed;
        }

        /// <summary>The flares of one drop still waiting to fire.</summary>
        private sealed class Salvo
        {
            public Entity Chopper;
            public int Left;
            public int Index;
            public float Wait;
            /// <summary>Seconds the flares burn: the server's burn time (FlareRemaining) when sent, else the settings.</summary>
            public float Burn;
        }

        private readonly Dictionary<Entity, uint> _lastCount = new();
        private readonly Dictionary<Entity, uint> _seen = new();
        private readonly List<Flare> _flares = new();
        private readonly List<Salvo> _salvos = new();
        private CombatEffects _effects;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            if (_effects != context.Effects)
            {
                _flares.Clear();
                _salvos.Clear();
                _effects = context.Effects;
            }
            var settings = _effects.Settings;
            if (settings == null) return;

            var deltaTime = UnityEngine.Time.deltaTime;
            MoveFlares(deltaTime);
            FireSalvos(settings, deltaTime);

            _seen.Clear();
            foreach (var (countermeasure, pose, entity) in SystemAPI
                         .Query<RefRO<CountermeasureState>, RefRO<LocalToWorld>>()
                         .WithEntityAccess())
            {
                var count = countermeasure.ValueRO.FlareCount;
                _seen[entity] = count;
                if (!_lastCount.TryGetValue(entity, out var last) || count <= last) continue;

                var burn = countermeasure.ValueRO.FlareRemaining > 0f ? countermeasure.ValueRO.FlareRemaining : settings.FlareLifetime;
                _salvos.Add(new Salvo { Chopper = entity, Left = settings.FlaresPerDrop, Burn = burn });
                if (context.Sounds != null) context.Audio.Play(context.Sounds.Flares, pose.ValueRO.Position);
            }

            _lastCount.Clear();
            foreach (var (entity, count) in _seen) _lastCount[entity] = count;
        }

        /// <summary>Fires the next flare of each salvo when its wait runs out, from where the chopper is now.</summary>
        private void FireSalvos(CombatEffectsSettings settings, float deltaTime)
        {
            var poses = SystemAPI.GetComponentLookup<LocalToWorld>(true);
            var observed = SystemAPI.GetComponentLookup<ObservedVelocity>(true);
            var physics = SystemAPI.GetComponentLookup<PhysicsVelocity>(true);
            for (var i = _salvos.Count - 1; i >= 0; i--)
            {
                var salvo = _salvos[i];
                salvo.Wait -= deltaTime;
                while (salvo.Left > 0 && salvo.Wait <= 0f)
                {
                    if (!poses.HasComponent(salvo.Chopper)) { salvo.Left = 0; break; }
                    Vector3 velocity = ObservedVelocity.Of(salvo.Chopper, observed, physics);
                    Launch(settings, poses[salvo.Chopper], velocity, salvo.Index++, salvo.Burn);
                    salvo.Left--;
                    salvo.Wait += Mathf.Max(0.01f, settings.FlareInterval) * Random.Range(0.85f, 1.2f);
                }
                if (salvo.Left <= 0) _salvos.RemoveAt(i);
            }
        }

        /// <summary>One flare kicks out sideways (left and right in turn) and up a little, then drag slows it to a hang.</summary>
        private void Launch(CombatEffectsSettings settings, in LocalToWorld pose, Vector3 chopperVelocity, int index, float burn)
        {
            if (settings.Flare == null) return;
            Vector3 right = pose.Right;
            Vector3 up = pose.Up;
            Vector3 back = -(Vector3)(float3)pose.Forward;
            Vector3 origin = pose.Position - pose.Up * 0.8f;

            var side = index % 2 == 0 ? 1f : -1f;
            var velocity = chopperVelocity * 0.6f
                           + right * (side * Random.Range(14f, 22f))
                           + up * Random.Range(-2f, 5f)
                           + back * Random.Range(4f, 9f);
            var visual = _effects.SpawnPooled(settings.Flare, origin, Quaternion.LookRotation(velocity.normalized));
            var lifetime = burn * Random.Range(0.9f, 1.1f);
            _flares.Add(new Flare
            {
                Visual = visual, Position = origin, Velocity = velocity, Remaining = lifetime, Lifetime = lifetime,
                BaseScale = visual != null ? visual.transform.localScale : Vector3.one, Seed = Random.value * 100f,
            });
        }

        private void MoveFlares(float deltaTime)
        {
            var linger = _effects.Settings != null ? _effects.Settings.RocketTrailLinger : 1f;
            for (var i = _flares.Count - 1; i >= 0; i--)
            {
                var flare = _flares[i];
                flare.Velocity += Vector3.down * (FlareGravity * deltaTime);
                flare.Velocity *= Mathf.Exp(-FlareDrag * deltaTime);
                flare.Position += flare.Velocity * deltaTime;
                flare.Remaining -= deltaTime;
                if (flare.Visual != null)
                {
                    flare.Visual.transform.position = flare.Position;
                    flare.Visual.transform.localScale = flare.BaseScale * Fizz(flare);
                }
                if (flare.Remaining > 0f) continue;

                if (flare.Visual != null) flare.Visual.transform.localScale = flare.BaseScale;
                _effects.Release(flare.Visual, linger);
                _flares.RemoveAt(i);
            }
        }

        /// <summary>Size of a burning flare: a bright flash at launch, a fast flicker, then it shrinks as it burns out.</summary>
        private static float Fizz(Flare flare)
        {
            var age = flare.Lifetime - flare.Remaining;
            var flash = 1f + 0.8f * Mathf.Exp(-age / 0.12f);
            var flicker = 0.75f + 0.5f * Mathf.PerlinNoise(flare.Seed, age * FizzRate);
            var burnOut = Mathf.Clamp01(flare.Remaining / (flare.Lifetime * 0.35f));
            return flash * flicker * Mathf.Lerp(0.25f, 1f, burnOut);
        }
    }
}
