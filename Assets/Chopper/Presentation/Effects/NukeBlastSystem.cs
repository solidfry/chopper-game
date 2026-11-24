using System;
using System.Collections.Generic;
using Chopper.Presentation.Bridge;
using Chopper.Presentation.Events;
using Chopper.Simulation.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Physics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Client only. Capture the Nuke: the nuke going off in a base (<see cref="NukeDetonated"/>), with the parts of a
    /// tactical nuke (owner, 24 Nov). The blast prefab (<see cref="CombatEffectsSettings.NukeBlast"/>) on the ground
    /// under the burst, animated by <see cref="NukeBlastTimeline"/>: the fireball swells and rises into the mushroom,
    /// its lava cracks cool, the stem draws up, shock rings run out round the cap, wind strokes and dust puffs run out on
    /// the ground, then it all dissolves. On top: a screen flash (stronger when you look at it), a light that fades
    /// white to orange, the EMP (HUD glitch near the blast, at the flash), and the shake when the shock front reaches
    /// the camera (at <see cref="CombatEffectsSettings.NukeShockSpeed"/>), plus pack ground fires. Looks only.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class NukeBlastSystem : PresentationSystemBase
    {
        private const float GroundSearch = 400f;
        /// <summary>Lowest and highest burst height used for the fireball (m, prefab units).</summary>
        private const float MinBurst = 18f, MaxBurst = 120f;
        private const float EmpSeconds = 1.8f;
        private const float FlashFarDistance = 3000f;

        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int BaseColourId = Shader.PropertyToID("_BaseColor");

        /// <summary>One blast in progress.</summary>
        private sealed class Blast
        {
            public GameObject Root;
            public float Start;
            public float Burst;
            public Vector3 Centre;
            public bool ShockArrived;
            public float ShockAt;
            public Transform Rise, Cap, Stem, Scorch;
            public Transform[] Rings = Array.Empty<Transform>(), Puffs = Array.Empty<Transform>(), Swirls = Array.Empty<Transform>();
            public Renderer CapRenderer, StemRenderer, ScorchRenderer;
            public Renderer[] RingRenderers = Array.Empty<Renderer>(), PuffRenderers = Array.Empty<Renderer>(), SwirlRenderers = Array.Empty<Renderer>();
            public Light Light;
            public GameObject Flash;
            public Renderer FlashRenderer;
            public float FlashShare;
        }

        private readonly List<NukeDetonated> _incoming = new();
        private readonly List<Blast> _blasts = new();
        private readonly List<(float At, Vector3 Position, int Layer)> _pending = new();
        private MaterialPropertyBlock _block;
        private PresentationContext _context;
        private IDisposable _subscription;

        protected override void OnCreate()
        {
            base.OnCreate();
            RequireForUpdate<NetworkStreamInGame>();
        }

        protected override void OnDestroy()
        {
            _subscription?.Dispose();
            ClearBlasts();
        }

        protected override void OnUpdate()
        {
            if (!TryGetContext(out var context)) return;
            Bind(context);
            var effects = context.Effects;
            var settings = effects.Settings;
            if (settings == null) return;
            var now = UnityEngine.Time.time;
            _block ??= new MaterialPropertyBlock();

            foreach (var detonation in _incoming) Begin(context, settings, detonation, now);
            _incoming.Clear();

            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].At > now) continue;
                var (_, position, layer) = _pending[i];
                _pending.RemoveAt(i);
                if (layer < settings.NukeBlastLayers.Count) effects.SpawnLayer(settings.NukeBlastLayers[layer], position);
            }

            for (var i = _blasts.Count - 1; i >= 0; i--)
            {
                var blast = _blasts[i];
                var t = now - blast.Start;
                if (t > NukeBlastTimeline.Total(settings.NukeBlastSeconds, settings.NukeFadeSeconds))
                {
                    End(blast);
                    _blasts.RemoveAt(i);
                    continue;
                }
                Animate(blast, settings, t);
                Lights(blast, settings, t);
                if (!blast.ShockArrived && t >= blast.ShockAt)
                {
                    blast.ShockArrived = true;
                    Shake(context, settings, blast.Centre);
                }
            }
        }

        private void Begin(PresentationContext context, CombatEffectsSettings settings, NukeDetonated detonation, float now)
        {
            var ground = FindGround(detonation.Position);
            var layers = settings.NukeBlastLayers;
            for (var i = 0; i < layers.Count; i++)
                _pending.Add((now + layers[i].Delay, layers[i].Ground ? ground : detonation.Position, i));
            context.HeatMarks.AddTimed(detonation.Position, VisionMath.BlastHeat * 6f, VisionMath.BlastSeconds * 6f);

            var camera = UnityEngine.Camera.main;
            var distance = camera != null ? Vector3.Distance(camera.transform.position, detonation.Position) : float.MaxValue;
            // EMP: at the speed of light, so at the flash.
            if (settings.NukeEmpRadius > 0f && distance < settings.NukeEmpRadius)
                context.HudLook?.Disrupt(Mathf.Lerp(1f, 0.4f, distance / settings.NukeEmpRadius), EmpSeconds);

            var scale = settings.NukeBlastRadius / NukeBlastTimeline.BaseRadius;
            var blast = new Blast
            {
                Start = now,
                Centre = detonation.Position,
                Burst = Mathf.Clamp((detonation.Position.y - ground.y) / scale, MinBurst, MaxBurst),
                ShockAt = NukeBlastTimeline.ShockArrival(distance, settings.NukeShockSpeed),
            };
            if (settings.NukeBlast != null)
            {
                blast.Root = Object.Instantiate(settings.NukeBlast, ground, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                blast.Root.transform.localScale = Vector3.one * scale;
                Find(blast);
                var seed = UnityEngine.Random.Range(0f, 100f);
                Seed(blast.CapRenderer, seed);
                Seed(blast.StemRenderer, seed + 3f);
                Seed(blast.ScorchRenderer, seed);
                for (var i = 0; i < blast.PuffRenderers.Length; i++) Seed(blast.PuffRenderers[i], seed + 10f + i * 1.7f);
                for (var i = 0; i < blast.RingRenderers.Length; i++) Seed(blast.RingRenderers[i], seed + 40f + i * 2.3f);
                for (var i = 0; i < blast.SwirlRenderers.Length; i++) Seed(blast.SwirlRenderers[i], seed + 60f + i * 3.1f);
                Animate(blast, settings, 0f);
            }
            blast.Light = MakeLight(settings, detonation.Position);
            if (camera != null && settings.NukeScreenFlash != null)
            {
                var toBlast = (detonation.Position - camera.transform.position).normalized;
                var facing = Mathf.Clamp01(Vector3.Dot(camera.transform.forward, toBlast) * 0.5f + 0.5f);
                blast.FlashShare = (1f - Mathf.Clamp01(distance / FlashFarDistance)) * (0.35f + 0.65f * facing);
                MakeFlash(blast, settings, camera);
            }
            _blasts.Add(blast);
        }

        private void Animate(Blast blast, CombatEffectsSettings settings, float t)
        {
            if (blast.Root == null) return;
            var fade = NukeBlastTimeline.Fade(t, settings.NukeBlastSeconds, settings.NukeFadeSeconds);
            var capY = NukeBlastTimeline.CapHeight(t, blast.Burst, settings.NukeRiseHeight, settings.NukeRiseSeconds);
            var capR = NukeBlastTimeline.CapRadius(t);
            if (blast.Rise != null) blast.Rise.localPosition = new Vector3(0f, capY, 0f);
            if (blast.Cap != null)
            {
                var spread = NukeBlastTimeline.CapSpread(t);
                blast.Cap.localScale = new Vector3(capR * spread, capR, capR * spread);
                Set(blast.CapRenderer, NukeBlastTimeline.CapHeat(t), fade);
            }

            if (blast.Stem != null)
            {
                var grow = NukeBlastTimeline.StemGrow(t);
                var width = NukeBlastTimeline.BaseRadius * 1.0f * grow;
                blast.Stem.localScale = new Vector3(width, NukeBlastTimeline.StemHeight(capY, capR), width);
                Set(blast.StemRenderer, NukeBlastTimeline.StemHeat(t), fade);
            }

            for (var i = 0; i < blast.Rings.Length; i++)
            {
                var on = NukeBlastTimeline.Ring(i, t, out var radius, out var ringFade);
                blast.RingRenderers[i].enabled = on;
                if (!on) continue;
                blast.Rings[i].localScale = Vector3.one * radius;
                blast.Rings[i].localRotation = Quaternion.Euler((i - 1) * 7f, i * 40f, i % 2 == 0 ? 5f : -6f);
                Fade(blast.RingRenderers[i], ringFade);
            }

            for (var i = 0; i < blast.Swirls.Length; i++)
            {
                var on = NukeBlastTimeline.Swirl(i, t, out var radius, out var swirlFade, out var spin);
                blast.SwirlRenderers[i].enabled = on;
                if (!on) continue;
                blast.Swirls[i].localPosition = new Vector3(0f, 1.5f + i * 0.4f, 0f);
                blast.Swirls[i].localScale = Vector3.one * radius;
                blast.Swirls[i].localRotation = Quaternion.Euler(0f, spin, 0f);
                Fade(blast.SwirlRenderers[i], swirlFade);
            }

            for (var i = 0; i < blast.Puffs.Length; i++)
            {
                NukeBlastTimeline.Puff(i, t, out var distance, out var size, out var heat);
                var angle = NukeBlastTimeline.PuffAngle(i) * Mathf.Deg2Rad;
                blast.Puffs[i].localPosition = new Vector3(Mathf.Cos(angle) * distance, size * 0.25f, Mathf.Sin(angle) * distance);
                blast.Puffs[i].localScale = new Vector3(Mathf.Max(size, 0.01f), Mathf.Max(size * 0.85f, 0.01f), Mathf.Max(size, 0.01f));
                blast.PuffRenderers[i].enabled = size > 0.01f;
                Set(blast.PuffRenderers[i], heat, fade);
            }

            if (blast.Scorch != null)
            {
                NukeBlastTimeline.Scorch(t, out var size, out var heat);
                blast.Scorch.localPosition = new Vector3(0f, 0.3f, 0f);
                blast.Scorch.localScale = new Vector3(Mathf.Max(size, 0.01f), 1f, Mathf.Max(size, 0.01f));
                var scorchFade = NukeBlastTimeline.Fade(t, settings.NukeBlastSeconds + settings.NukeFadeSeconds, NukeBlastTimeline.ScorchLinger);
                Set(blast.ScorchRenderer, heat, scorchFade);
            }
        }

        private void Lights(Blast blast, CombatEffectsSettings settings, float t)
        {
            if (blast.Light != null)
            {
                var k = t / settings.NukeFlashSeconds;
                if (k >= 1f)
                {
                    Object.Destroy(blast.Light.gameObject);
                    blast.Light = null;
                }
                else
                {
                    blast.Light.intensity = settings.NukeFlashIntensity * (1f - k) * (1f - k);
                    blast.Light.color = Color.Lerp(settings.NukeFlashColour, new Color(1f, 0.45f, 0.15f), k);
                }
            }
            if (blast.Flash != null)
            {
                if (t > 2.5f)
                {
                    Object.Destroy(blast.Flash);
                    blast.Flash = null;
                }
                else
                {
                    _block.Clear();
                    _block.SetColor(BaseColourId, new Color(1f, 0.97f, 0.9f, NukeBlastTimeline.ScreenFlash(t) * blast.FlashShare));
                    blast.FlashRenderer.SetPropertyBlock(_block);
                }
            }
        }

        private static void Find(Blast blast)
        {
            var root = blast.Root.transform;
            blast.Rise = root.Find("Rise");
            blast.Cap = root.Find("Rise/Cap");
            blast.Stem = root.Find("Stem");
            blast.Scorch = root.Find("Scorch");
            blast.CapRenderer = blast.Cap != null ? blast.Cap.GetComponent<Renderer>() : null;
            blast.StemRenderer = blast.Stem != null ? blast.Stem.GetComponent<Renderer>() : null;
            blast.ScorchRenderer = blast.Scorch != null ? blast.Scorch.GetComponent<Renderer>() : null;
            blast.Rings = Children(root.Find("Rise/Rings"));
            blast.Puffs = Children(root.Find("Puffs"));
            blast.Swirls = Children(root.Find("Swirls"));
            blast.RingRenderers = Array.ConvertAll(blast.Rings, r => r.GetComponent<Renderer>());
            blast.PuffRenderers = Array.ConvertAll(blast.Puffs, r => r.GetComponent<Renderer>());
            blast.SwirlRenderers = Array.ConvertAll(blast.Swirls, r => r.GetComponent<Renderer>());
        }

        private static Transform[] Children(Transform parent)
        {
            if (parent == null) return Array.Empty<Transform>();
            var children = new Transform[parent.childCount];
            for (var i = 0; i < children.Length; i++) children[i] = parent.GetChild(i);
            return children;
        }

        private void Seed(Renderer renderer, float seed)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(_block);
            _block.SetFloat(SeedId, seed);
            renderer.SetPropertyBlock(_block);
        }

        private void Fade(Renderer renderer, float fade)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(_block);
            _block.SetFloat(FadeId, fade);
            renderer.SetPropertyBlock(_block);
        }

        private void Set(Renderer renderer, float heat, float fade)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(_block);
            _block.SetFloat(HeatId, heat);
            _block.SetFloat(FadeId, fade);
            renderer.SetPropertyBlock(_block);
        }

        private static Light MakeLight(CombatEffectsSettings settings, Vector3 position)
        {
            if (settings.NukeFlashIntensity <= 0f) return null;
            var go = new GameObject("NukeFlashLight");
            go.transform.position = position + Vector3.up * 30f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = settings.NukeFlashRange;
            light.color = settings.NukeFlashColour;
            light.intensity = settings.NukeFlashIntensity;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>A white quad just in front of the camera: the screen goes white, then clears.</summary>
        private static void MakeFlash(Blast blast, CombatEffectsSettings settings, UnityEngine.Camera camera)
        {
            if (blast.FlashShare <= 0.01f) return;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "NukeScreenFlash";
            Object.Destroy(quad.GetComponent<UnityEngine.Collider>());
            quad.transform.SetParent(camera.transform, false);
            var distance = camera.nearClipPlane + 0.05f;
            var height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.2f;
            quad.transform.localPosition = new Vector3(0f, 0f, distance);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(height * camera.aspect, height, 1f);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = settings.NukeScreenFlash;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            blast.Flash = quad;
            blast.FlashRenderer = renderer;
        }

        /// <summary>The first static surface below the blast; the blast point when there is none.</summary>
        private Vector3 FindGround(Vector3 position)
        {
            if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics)) return position;
            var world = physics.CollisionWorld;
            var hits = new NativeList<Unity.Physics.RaycastHit>(8, Allocator.Temp);
            try
            {
                var ray = new RaycastInput { Start = position, End = position + Vector3.down * GroundSearch, Filter = CollisionFilter.Default };
                if (!world.CastRay(ray, ref hits)) return position;
                var best = 2f;
                var point = position;
                foreach (var hit in hits)
                {
                    if (hit.RigidBodyIndex < world.NumDynamicBodies || hit.Fraction >= best) continue;
                    best = hit.Fraction;
                    point = hit.Position;
                }
                return point;
            }
            finally
            {
                hits.Dispose();
            }
        }

        private static void Shake(PresentationContext context, CombatEffectsSettings settings, Vector3 position)
        {
            var rig = context.CameraRig;
            if (rig == null) return;
            var distance = Vector3.Distance(rig.transform.position, position);
            var share = 1f - Mathf.Clamp01(distance / settings.NukeBlastShakeRange);
            if (share > 0f) rig.AddShake(settings.NukeBlastShake * share);
        }

        private static void End(Blast blast)
        {
            if (blast.Root != null) Object.Destroy(blast.Root);
            if (blast.Light != null) Object.Destroy(blast.Light.gameObject);
            if (blast.Flash != null) Object.Destroy(blast.Flash);
        }

        private void ClearBlasts()
        {
            foreach (var blast in _blasts) End(blast);
            _blasts.Clear();
        }

        /// <summary>New context (scene change): pending layers and blasts belonged to the old scene.</summary>
        private void Bind(PresentationContext context)
        {
            if (_context == context) return;
            _subscription?.Dispose();
            _incoming.Clear();
            _pending.Clear();
            ClearBlasts();
            _context = context;
            _subscription = context.Events.Subscribe<NukeDetonated>(_incoming.Add);
        }
    }
}
