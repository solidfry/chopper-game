using System;
using System.Collections.Generic;
using Chopper.Presentation.Visuals;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Chopper.Simulation.Combat;
using Unity.Physics;
using UnityEngine;
using Material = UnityEngine.Material;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only, cosmetic. The break-up of a wreck (Docs/ChopperDeathDesign.md, step 2). While a wreck falls, its
    /// render parts (mesh, materials, skin row, world matrix) are kept up to date. When it explodes, each part becomes a
    /// piece that keeps the wreck's velocity, is thrown out from the blast and spins; it bounces on the level (raycast
    /// on the client physics world), trails smoke (big pieces also burn for a while), then sinks and goes. Owned by
    /// <see cref="CombatEffects"/>; <see cref="DamageEffectsSystem"/> drives it and gives it the trail emitters,
    /// <see cref="Visuals.ModuleVisualSystem"/> adds the module art.
    /// </summary>
    public sealed class WreckDebris : IDisposable
    {
        /// <summary>A wreck whose ghost is gone still matches a blast for this long (the RPC can come after the despawn).</summary>
        private const float KeepAfterGone = 1f;
        private const float MatchRange = 25f;
        private const int MaxPieces = 24;
        /// <summary>Chunks the hull mesh is cut into.</summary>
        private const int HullChunks = 7;
        private const int MaxLive = 72;
        private const float Gravity = 9.81f;

        public delegate void Emit(Vector3 point, Vector3 velocity, float strength, int count);

        private sealed class Part
        {
            public Mesh Mesh;
            public Material[] Materials;
            public float SkinRow;
            public bool HasSkin;
            public Matrix4x4 Matrix;
        }

        private sealed class Tracked
        {
            /// <summary>The entity render parts (the hull), in LinkedEntityGroup order.</summary>
            public readonly List<Part> Parts = new();
            /// <summary>Module art (rotors, weapons, pods): GameObjects of ModuleVisualSystem, read each frame.</summary>
            public readonly List<Part> Visuals = new();
            public bool Read;
            public float LastSeen;
        }

        private sealed class Piece
        {
            public GameObject Object;
            public Vector3 Velocity;
            public Vector3 Spin;
            public float Age;
            public float Life;
            public float Burn;
            public float Size;
            public float SmokeCarry;
            public float FlameCarry;
            public bool Resting;
        }

        private readonly Transform _root;
        private Emit _smoke;
        private Emit _flames;
        private Emit _sparks;
        private readonly Dictionary<Entity, Tracked> _tracked = new();
        private readonly List<Piece> _pieces = new();
        private readonly List<Entity> _gone = new();
        private readonly List<Part> _burst = new();
        private readonly List<MeshRenderer> _renderers = new();
        private readonly MaterialPropertyBlock _block = new();
        private readonly MeshShatter _shatter = new();

        public WreckDebris(Transform parent)
        {
            _root = new GameObject("Wreck Debris").transform;
            _root.SetParent(parent, false);
        }

        /// <summary>The smoke, flame and spark emitters of <see cref="DamageEffectsSystem"/> (null: no trails).</summary>
        public void SetTrails(Emit smoke, Emit flames, Emit sparks)
        {
            _smoke = smoke;
            _flames = flames;
            _sparks = sparks;
        }

        /// <summary>Keeps a falling wreck's render parts and their world matrices. Reads the parts once.</summary>
        public void Track(EntityManager entities, Entity wreck, float now)
        {
            var tracked = Get(wreck, now);
            if (!tracked.Read)
            {
                tracked.Read = true;
                Read(entities, wreck, tracked);
            }
            tracked.LastSeen = now;
            var index = 0;
            if (!entities.HasBuffer<LinkedEntityGroup>(wreck)) return;
            var group = entities.GetBuffer<LinkedEntityGroup>(wreck, true);
            for (var i = 0; i < group.Length && index < tracked.Parts.Count; i++)
            {
                var target = group[i].Value;
                if (!IsRendered(entities, target)) continue;
                tracked.Parts[index++].Matrix = entities.GetComponentData<Unity.Transforms.LocalToWorld>(target).Value;
            }
        }

        /// <summary>A falling wreck's module art (called by ModuleVisualSystem each frame, after its parts are placed).</summary>
        public void TrackVisuals(Entity wreck, float now, IEnumerable<GameObject> visuals)
        {
            var tracked = Get(wreck, now);
            tracked.Visuals.Clear();
            foreach (var visual in visuals)
            {
                if (visual == null || !visual.activeInHierarchy) continue;
                visual.GetComponentsInChildren(false, _renderers);
                foreach (var renderer in _renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    var part = new Part { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, Matrix = renderer.localToWorldMatrix };
                    if (renderer.HasPropertyBlock())
                    {
                        renderer.GetPropertyBlock(_block);
                        part.HasSkin = true;
                        part.SkinRow = _block.GetFloat(SkinLibrary.SkinIndexProperty);
                    }
                    tracked.Visuals.Add(part);
                }
            }
            _renderers.Clear();
        }

        private Tracked Get(Entity wreck, float now)
        {
            if (!_tracked.TryGetValue(wreck, out var tracked)) _tracked[wreck] = tracked = new Tracked();
            tracked.LastSeen = now;
            return tracked;
        }

        /// <summary>
        /// The wreck nearest the blast breaks up; its pieces start from where its parts were last seen. The biggest
        /// <paramref name="heavyParts"/> pieces fly as the server's heavy parts do (same seed, <see cref="WreckParts.Launch"/>),
        /// so the piece you see is the one that hits.
        /// </summary>
        public void Burst(Vector3 position, Vector3 velocity, uint partSeed = 0, int heavyParts = 0)
        {
            Tracked best = null;
            var bestEntity = Entity.Null;
            var bestDistance = MatchRange * MatchRange;
            foreach (var (entity, tracked) in _tracked)
            {
                var list = tracked.Parts.Count > 0 ? tracked.Parts : tracked.Visuals;
                if (list.Count == 0) continue;
                var distance = ((Vector3)list[0].Matrix.GetColumn(3) - position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = tracked;
                bestEntity = entity;
            }

            _sparks?.Invoke(position, velocity, 1f, 90);
            if (best == null) return;
            _tracked.Remove(bestEntity);

            // The hull is cut into chunks (nose, cabin, boom, tail...); module art flies whole.
            _burst.Clear();
            foreach (var part in best.Parts)
            {
                if (part.Mesh == null || part.Materials == null || part.Materials.Length == 0) continue;
                var chunks = _shatter.Cut(part.Mesh, HullChunks);
                if (chunks == null) { _burst.Add(part); continue; }
                foreach (var (chunk, centre) in chunks)
                    _burst.Add(new Part
                    {
                        Mesh = chunk, Materials = part.Materials, HasSkin = part.HasSkin, SkinRow = part.SkinRow,
                        Matrix = part.Matrix * Matrix4x4.Translate(centre),
                    });
            }
            foreach (var part in best.Visuals)
                if (part.Materials != null && part.Materials.Length > 0) _burst.Add(part);

            // Biggest pieces first: they carry the fire; tiny bits hide in the blast.
            _burst.Sort((a, b) => Size(b).CompareTo(Size(a)));
            var count = Mathf.Min(_burst.Count, MaxPieces);
            for (var i = 0; i < count; i++)
            {
                while (_pieces.Count >= MaxLive) Remove(0);
                _pieces.Add(Spawn(_burst[i], position, velocity, i, partSeed, i < heavyParts ? heavyParts : 0));
            }
            _burst.Clear();
        }

        /// <summary>
        /// Cuts these chopper models' hulls now (all their meshes), so the first break-up of each type has no hitch
        /// (26 Nov: 6-26 ms per chassis on its first wreck). Call under the loading screen.
        /// </summary>
        public void Prewarm(IEnumerable<GameObject> models)
        {
            if (models == null) return;
            var filters = new List<MeshFilter>();
            foreach (var model in models)
            {
                if (model == null) continue;
                model.GetComponentsInChildren(true, filters);
                foreach (var filter in filters) _shatter.Cut(filter.sharedMesh, HullChunks);
            }
        }

        /// <summary>A ground ray for scenes without ECS physics (the effects bench): true = hit at point, normal.</summary>
        public delegate bool GroundRay(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal);

        public void Tick(float deltaTime, float now, in CollisionWorld world, bool hasWorld) => Step(deltaTime, now, world, hasWorld, null);

        /// <summary>The bench: pieces bounce on what <paramref name="ray"/> hits (scene colliders).</summary>
        public void Tick(float deltaTime, float now, GroundRay ray) => Step(deltaTime, now, default, false, ray);

        private void Step(float deltaTime, float now, in CollisionWorld world, bool hasWorld, GroundRay ray)
        {
            _gone.Clear();
            foreach (var (entity, tracked) in _tracked)
                if (now - tracked.LastSeen > KeepAfterGone) _gone.Add(entity);
            foreach (var entity in _gone) _tracked.Remove(entity);

            if (deltaTime <= 0f) return;
            for (var i = _pieces.Count - 1; i >= 0; i--)
            {
                _pieces[i].Age += deltaTime;
                if (_pieces[i].Age >= _pieces[i].Life + DebrisMotion.SinkSeconds) Remove(i);
            }

            // Moving pieces: this frame's step as rays. In a match all rays go in one Burst job (26 Nov: up to 72 managed
            // rays a frame after a few wrecks); the bench uses its scene ray.
            var rays = hasWorld ? new NativeArray<RaycastInput>(_pieces.Count, Allocator.TempJob) : default;
            var hits = hasWorld ? new NativeArray<GroundHit>(_pieces.Count, Allocator.TempJob) : default;
            for (var i = 0; i < _pieces.Count; i++)
            {
                var piece = _pieces[i];
                var from = piece.Object.transform.position;
                if (!piece.Resting)
                    piece.Velocity = DebrisMotion.Drag(piece.Velocity, piece.Size, deltaTime) + Vector3.down * (Gravity * deltaTime);
                var to = piece.Resting ? from : from + piece.Velocity * deltaTime;
                if (hasWorld) rays[i] = new RaycastInput { Start = from, End = to, Filter = CollisionFilter.Default };
            }
            if (hasWorld) new GroundRaysJob { World = world, Rays = rays, Hits = hits }.Run();

            for (var i = 0; i < _pieces.Count; i++)
            {
                var piece = _pieces[i];
                var transform = piece.Object.transform;
                if (!piece.Resting)
                {
                    var from = transform.position;
                    var to = from + piece.Velocity * deltaTime;
                    Vector3 point = default, normal = default;
                    bool hit;
                    if (hasWorld)
                    {
                        hit = hits[i].Hit;
                        point = hits[i].Point;
                        normal = hits[i].Normal;
                    }
                    else hit = ray != null && ray(from, to, out point, out normal);
                    if (hit)
                    {
                        piece.Velocity = DebrisMotion.Bounce(piece.Velocity, normal, out var rest);
                        piece.Spin *= 0.5f;
                        // Not at once: a blast on the ground first throws its pieces up and along.
                        piece.Resting = rest && piece.Age > DebrisMotion.SettleAfter;
                        to = point + normal * 0.05f;
                        if (piece.Burn > 0f) _sparks?.Invoke(point, normal * 3f, 0.4f, 6);
                    }
                    transform.position = to;
                    transform.rotation = Quaternion.Euler(piece.Spin * (Mathf.Rad2Deg * deltaTime)) * transform.rotation;
                }
                if (piece.Age > piece.Life)
                    transform.position += Vector3.down * (piece.Size * deltaTime / DebrisMotion.SinkSeconds);

                // Trails: fire for the first seconds on big pieces, smoke that thins out over the life.
                var heat = DebrisMotion.Heat(piece.Age, piece.Burn);
                var smoke = DebrisMotion.Smoke(piece.Age, piece.Life);
                piece.FlameCarry += 30f * heat * deltaTime;
                piece.SmokeCarry += 14f * smoke * Mathf.Clamp(piece.Size, 0.4f, 2f) * deltaTime;
                var flames = (int)piece.FlameCarry;
                var puffs = (int)piece.SmokeCarry;
                piece.FlameCarry -= flames;
                piece.SmokeCarry -= puffs;
                var trailVelocity = piece.Resting ? Vector3.zero : piece.Velocity;
                if (flames > 0) _flames?.Invoke(transform.position, trailVelocity, heat, flames);
                if (puffs > 0) _smoke?.Invoke(transform.position, trailVelocity, smoke, puffs);
            }
            if (hasWorld)
            {
                rays.Dispose();
                hits.Dispose();
            }
        }

        private struct GroundHit
        {
            public bool Hit;
            public float3 Point;
            public float3 Normal;
        }

        /// <summary>The nearest hit of each ray (zero-length rays: none).</summary>
        [BurstCompile]
        private struct GroundRaysJob : IJob
        {
            [ReadOnly] public CollisionWorld World;
            [ReadOnly] public NativeArray<RaycastInput> Rays;
            public NativeArray<GroundHit> Hits;

            public void Execute()
            {
                for (var i = 0; i < Rays.Length; i++)
                {
                    var ray = Rays[i];
                    if (math.lengthsq(ray.End - ray.Start) > 1e-8f && World.CastRay(ray, out var hit))
                        Hits[i] = new GroundHit { Hit = true, Point = hit.Position, Normal = hit.SurfaceNormal };
                    else
                        Hits[i] = default;
                }
            }
        }

        public void Dispose()
        {
            _tracked.Clear();
            _pieces.Clear();
            _shatter.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
        }

        private Piece Spawn(Part part, Vector3 blast, Vector3 velocity, int rank, uint partSeed, int heavyParts)
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(_root, false);
            go.transform.SetPositionAndRotation(part.Matrix.GetColumn(3), part.Matrix.rotation);
            go.transform.localScale = part.Matrix.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = part.Materials;
            if (part.HasSkin)
            {
                _block.Clear();
                _block.SetFloat(SkinLibrary.SkinIndexProperty, part.SkinRow);
                renderer.SetPropertyBlock(_block);
            }

            var size = Size(part);
            var centre = renderer.bounds.center;
            var outward = centre - blast;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Random.onUnitSphere;
            var launch = DebrisMotion.Launch(velocity, outward, size, Random.value);
            if (heavyParts > 0)
            {
                // A server part: its start point and velocity (hidden in the flash, the jump is under a few metres).
                WreckParts.Launch(partSeed, rank, heavyParts, velocity, out var offset, out var partVelocity);
                go.transform.position += blast + (Vector3)offset - centre;
                launch = partVelocity;
            }
            return new Piece
            {
                Object = go,
                Velocity = launch,
                Spin = Random.onUnitSphere * DebrisMotion.SpinRate(size, Random.value),
                Life = Random.Range(7f, 10f),
                // The three biggest pieces burn longest.
                Burn = rank < 3 ? Random.Range(3f, 5f) : Random.Range(0.4f, 1.6f),
                Size = size,
            };
        }

        private void Remove(int index)
        {
            var piece = _pieces[index];
            if (piece.Object != null) Object.Destroy(piece.Object);
            _pieces.RemoveAt(index);
        }

        private void Read(EntityManager entities, Entity wreck, Tracked tracked)
        {
            if (!entities.HasBuffer<LinkedEntityGroup>(wreck)) return;
            var group = entities.GetBuffer<LinkedEntityGroup>(wreck, true).ToNativeArray(Allocator.Temp);
            foreach (var linked in group)
            {
                var target = linked.Value;
                if (!IsRendered(entities, target)) continue;
                var info = entities.GetComponentData<Unity.Rendering.MaterialMeshInfo>(target);
                var array = entities.GetSharedComponent<Unity.Rendering.RenderMeshArray>(target);
                var mesh = array.GetMesh(info);
                var materials = array.GetMaterials(info);
                var part = new Part { Mesh = mesh, Materials = materials?.ToArray() };
                if (entities.HasComponent<SkinIndexOverride>(target))
                {
                    part.HasSkin = true;
                    part.SkinRow = entities.GetComponentData<SkinIndexOverride>(target).Value;
                }
                // Kept even when it cannot be drawn, so Track's index stays in step with IsRendered; Spawn skips it.
                tracked.Parts.Add(part);
            }
            group.Dispose();
        }

        private static bool IsRendered(EntityManager entities, Entity target) =>
            entities.HasComponent<Unity.Rendering.MaterialMeshInfo>(target) &&
            entities.HasComponent<Unity.Transforms.LocalToWorld>(target) &&
            entities.HasComponent<Unity.Rendering.RenderMeshArray>(target) &&
            entities.IsComponentEnabled<Unity.Rendering.MaterialMeshInfo>(target);

        private static float Size(Part part) =>
            part.Mesh == null ? 0f : Vector3.Scale(part.Mesh.bounds.size, part.Matrix.lossyScale).magnitude;
    }
}
