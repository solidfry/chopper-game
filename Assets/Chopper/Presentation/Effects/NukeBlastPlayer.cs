using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chopper.Presentation.Effects
{
    /// <summary>
    /// Plays the Capture the Nuke blast prefab (<see cref="CombatEffectsSettings.NukeBlast"/>) over time, by
    /// <see cref="NukeBlastTimeline"/>: cap rise and growth, cooling cracks, stem, shock rings, wind strokes, dust puffs,
    /// burn mark, dissolve, plus the screen flash on the given camera and a fading light. Shared by the game
    /// (<see cref="NukeBlastSystem"/>: adds ground search, EMP, heat mark, pack layers, shake) and the effects bench
    /// (key N), as <see cref="BlastSequence"/> is for the chopper blast. Plain class: no ECS.
    /// </summary>
    public sealed class NukeBlastPlayer
    {
        /// <summary>Lowest and highest burst height used for the fireball (m, prefab units).</summary>
        private const float MinBurst = 18f, MaxBurst = 120f;
        private const float FlashFarDistance = 3000f;
        /// <summary>Fire light inside the cap, as a share of the flash light.</summary>
        private const float FireLightShare = 0.12f;
        /// <summary>How fast the chromatic split of the shock front fades (s).</summary>
        private const float ShockPulseSeconds = 0.35f;

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
            /// <summary>0..1 how much of the heat and shock reach the camera (by distance).</summary>
            public float HeatShare;
            public Light FireLight;
            public NukeScreenFx Screen;
        }

        private readonly List<Blast> _blasts = new();
        private MaterialPropertyBlock _blockStore;
        /// <summary>Made on first use: Unity does not allow it while a MonoBehaviour that owns this player is built.</summary>
        private MaterialPropertyBlock Block => _blockStore ??= new MaterialPropertyBlock();

        public int Count => _blasts.Count;

        /// <summary>Starts a blast: <paramref name="burst"/> is the detonation point, <paramref name="ground"/> the ground
        /// under it. <paramref name="camera"/> (may be null) gets the screen flash and sets when the shock arrives.</summary>
        public void Start(CombatEffectsSettings settings, Vector3 burst, Vector3 ground, UnityEngine.Camera camera, float now)
        {
            var distance = camera != null ? Vector3.Distance(camera.transform.position, burst) : float.MaxValue;
            var scale = settings.NukeBlastRadius / NukeBlastTimeline.BaseRadius;
            var blast = new Blast
            {
                Start = now,
                Centre = burst,
                Burst = Mathf.Clamp((burst.y - ground.y) / scale, MinBurst, MaxBurst),
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
            blast.Light = MakeLight(settings, burst);
            if (camera != null && settings.NukeScreenFlash != null)
            {
                var toBlast = (burst - camera.transform.position).normalized;
                var facing = Mathf.Clamp01(Vector3.Dot(camera.transform.forward, toBlast) * 0.5f + 0.5f);
                blast.FlashShare = (1f - Mathf.Clamp01(distance / FlashFarDistance)) * (0.35f + 0.65f * facing);
                MakeFlash(blast, settings, camera);
            }
            if (camera != null)
            {
                blast.HeatShare = 1f - Mathf.Clamp01(distance / FlashFarDistance);
                if (blast.HeatShare > 0.01f) blast.Screen = new NukeScreenFx();
            }
            if (blast.Rise != null) blast.FireLight = MakeFireLight(settings, blast.Rise);
            _blasts.Add(blast);
        }

        /// <summary>Moves every blast on; <paramref name="shockArrived"/> is called once per blast when its shock front
        /// reaches the camera (centre given).</summary>
        public void Tick(CombatEffectsSettings settings, float now, Action<Vector3> shockArrived = null)
        {
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
                if (blast.ShockArrived || t < blast.ShockAt) continue;
                blast.ShockArrived = true;
                shockArrived?.Invoke(blast.Centre);
            }
        }

        public void Clear()
        {
            foreach (var blast in _blasts) End(blast);
            _blasts.Clear();
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
            var heat = NukeBlastTimeline.CapHeat(t) * (1f - NukeBlastTimeline.Fade(t, settings.NukeBlastSeconds, settings.NukeFadeSeconds));
            // The fire inside the cap lights the ground and the stem for as long as it burns.
            if (blast.FireLight != null) blast.FireLight.intensity = settings.NukeFlashIntensity * FireLightShare * heat;
            if (blast.Screen != null)
            {
                var shock = t >= blast.ShockAt ? Mathf.Exp(-(t - blast.ShockAt) / ShockPulseSeconds) : 0f;
                blast.Screen.Tick(NukeBlastTimeline.ScreenFlash(t) * blast.FlashShare, blast.HeatShare * heat * heat * 0.9f,
                    blast.HeatShare * shock);
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
                    Block.Clear();
                    Block.SetColor(BaseColourId, new Color(1f, 0.97f, 0.9f, NukeBlastTimeline.ScreenFlash(t) * blast.FlashShare));
                    blast.FlashRenderer.SetPropertyBlock(Block);
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
            renderer.GetPropertyBlock(Block);
            Block.SetFloat(SeedId, seed);
            renderer.SetPropertyBlock(Block);
        }

        private void Fade(Renderer renderer, float fade)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(Block);
            Block.SetFloat(FadeId, fade);
            renderer.SetPropertyBlock(Block);
        }

        private void Set(Renderer renderer, float heat, float fade)
        {
            if (renderer == null) return;
            renderer.GetPropertyBlock(Block);
            Block.SetFloat(HeatId, heat);
            Block.SetFloat(FadeId, fade);
            renderer.SetPropertyBlock(Block);
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

        private static void End(Blast blast)
        {
            if (blast.Root != null) Object.Destroy(blast.Root);
            if (blast.Light != null) Object.Destroy(blast.Light.gameObject);
            if (blast.Flash != null) Object.Destroy(blast.Flash);
            blast.Screen?.Dispose();
        }

        /// <summary>An orange light inside the cap; it rises with it and dims as the fire cools.</summary>
        private static Light MakeFireLight(CombatEffectsSettings settings, Transform rise)
        {
            if (settings.NukeFlashIntensity <= 0f) return null;
            var go = new GameObject("NukeFireLight");
            go.transform.SetParent(rise, false);
            go.transform.localPosition = Vector3.down * 20f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = settings.NukeFlashRange * 0.7f;
            light.color = new Color(1f, 0.5f, 0.18f);
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            return light;
        }
    }
}
