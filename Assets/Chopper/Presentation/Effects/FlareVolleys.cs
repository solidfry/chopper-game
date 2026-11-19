using System.Collections.Generic;
using Chopper.Simulation.Combat;
using UnityEngine;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// The flare look: each drop fires its flares one by one, left and right in turn (in time with the pops of the flare
    /// sound), then they hang, fizz and burn out. Shared by the game (<see cref="FlareEffectsSystem"/>, keyed by chopper
    /// entity) and the effects bench (<see cref="EffectsBench"/>). Cosmetic: the server decides what flares do.
    /// </summary>
    /// <typeparam name="TKey">What dropped the flares; <see cref="PoseOf"/> turns it into a pose each launch.</typeparam>
    public sealed class FlareVolleys<TKey>
    {
        /// <summary>The dropper's world pose and velocity now; false = it is gone (the rest of its drop is skipped).</summary>
        public delegate bool PoseOf(TKey key, out Matrix4x4 pose, out Vector3 velocity);

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

        private sealed class Salvo
        {
            public TKey Key;
            public int Left;
            public int Index;
            public float Wait;
            public float Burn;
        }

        private readonly List<Flare> _flares = new();
        private readonly List<Salvo> _salvos = new();

        /// <summary>A drop of <paramref name="count"/> flares that burn <paramref name="burn"/> seconds.</summary>
        public void Drop(TKey key, int count, float burn) => _salvos.Add(new Salvo { Key = key, Left = count, Burn = burn });

        /// <summary>Forgets every flare (scene change: the pooled visuals went with the old effects).</summary>
        public void Clear()
        {
            _flares.Clear();
            _salvos.Clear();
        }

        public void Tick(CombatEffects effects, float deltaTime, PoseOf poseOf)
        {
            var settings = effects.Settings;
            if (settings == null) return;
            Move(effects, deltaTime);
            for (var i = _salvos.Count - 1; i >= 0; i--)
            {
                var salvo = _salvos[i];
                salvo.Wait -= deltaTime;
                while (salvo.Left > 0 && salvo.Wait <= 0f)
                {
                    if (!poseOf(salvo.Key, out var pose, out var velocity)) { salvo.Left = 0; break; }
                    Launch(effects, settings, pose, velocity, salvo.Index++, salvo.Burn);
                    salvo.Left--;
                    salvo.Wait += Mathf.Max(0.01f, settings.FlareInterval) * Random.Range(0.85f, 1.2f);
                }
                if (salvo.Left <= 0) _salvos.RemoveAt(i);
            }
        }

        /// <summary>One flare kicks out sideways (left and right in turn) and up a little, then drag slows it to a hang.</summary>
        private void Launch(CombatEffects effects, CombatEffectsSettings settings, Matrix4x4 pose, Vector3 chopperVelocity, int index,
            float burn)
        {
            if (settings.Flare == null) return;
            Vector3 right = pose.GetColumn(0);
            Vector3 up = pose.GetColumn(1);
            Vector3 back = -(Vector3)pose.GetColumn(2);
            var origin = (Vector3)pose.GetColumn(3) - up * 0.8f;

            var side = index % 2 == 0 ? 1f : -1f;
            var velocity = chopperVelocity * 0.6f
                           + right.normalized * (side * Random.Range(14f, 22f))
                           + up.normalized * Random.Range(-2f, 5f)
                           + back.normalized * Random.Range(4f, 9f);
            var visual = effects.SpawnPooled(settings.Flare, origin, Quaternion.LookRotation(velocity.normalized));
            var lifetime = burn * Random.Range(0.9f, 1.1f);
            _flares.Add(new Flare
            {
                Visual = visual, Position = origin, Velocity = velocity, Remaining = lifetime, Lifetime = lifetime,
                BaseScale = visual != null ? visual.transform.localScale : Vector3.one, Seed = Random.value * 100f,
            });
        }

        private void Move(CombatEffects effects, float deltaTime)
        {
            var linger = effects.Settings != null ? effects.Settings.RocketTrailLinger : 1f;
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
                effects.Release(flare.Visual, linger);
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
