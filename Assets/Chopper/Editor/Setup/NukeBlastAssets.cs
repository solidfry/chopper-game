using System;
using System.Collections.Generic;
using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The Capture the Nuke blast prefab, built in code (owner, 24 Nov: "it needs all the characteristics of a tactical
    /// nuke", with a stylised reference: a solid puffy mushroom with glowing lava cracks, glowing shock rings round the
    /// cap, a ring of dust puffs, a burnt ground mark with streaks, white wind strokes, embers and debris).
    /// Meshes are made here (lumpy spheres and a lathe stem) and drawn with Art/Shaders/NukeCloud; rings with NukeRing;
    /// the burn with NukeScorch; the condensation shell and the shock bubble with ShockShell on particle spheres.
    /// <see cref="NukeBlastSystem"/> animates it (<see cref="NukeBlastTimeline"/>): rise, growth, cooling, rings, puffs,
    /// fade; and adds the screen flash, the light, the shock arrival (shake) and the EMP.
    /// Sizes are in metres for <see cref="NukeBlastTimeline.BaseRadius"/>; the system scales the root to the settings.
    /// Rebuilt when missing, or from the menu (materials and meshes are made again too).
    /// </summary>
    internal static class NukeBlastAssets
    {
        public const string PrefabPath = "Assets/Chopper/Prefabs/Effects/NukeBlast.prefab";
        private const string MeshFolder = "Assets/Chopper/Art/Effects/Nuke";
        private const string Folder = "Assets/Chopper/Data/Effects";
        public const string ScreenFlashPath = Folder + "/NukeScreenFlash.mat";

        /// <summary>The prefab; built if missing.</summary>
        public static GameObject Ensure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab != null ? prefab : Build(false);
        }

        [MenuItem("Chopper/Tools/Rebuild Nuke Blast")]
        private static void Rebuild()
        {
            if (Build(true) != null) Debug.Log($"Nuke blast rebuilt: {PrefabPath}");
        }

        /// <summary>White additive material for the screen flash (the system puts a quad in front of the camera).</summary>
        public static Material EnsureScreenFlash()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ScreenFlashPath);
            if (existing != null) return existing;
            var spark = EffectsAssets.EnsureSparkMaterial();
            if (spark == null) return null;
            var material = new Material(spark) { name = "NukeScreenFlash" };
            material.SetTexture("_BaseMap", null);
            material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, ScreenFlashPath);
            return material;
        }

        private static GameObject Build(bool fresh)
        {
            var cloud = Mat("NukeCloud", "Chopper/NukeCloud", fresh, _ => { });
            var debris = Mat("NukeDebris", "Chopper/NukeCloud", fresh, m =>
            {
                m.SetColor("_Colour", new Color(0.09f, 0.08f, 0.075f));
                m.SetColor("_HotColour", new Color(0.3f, 0.12f, 0.06f));
                m.SetFloat("_Heat", 0.2f);
                m.SetFloat("_Boil", 0f);
            });
            var ringGlow = Mat("NukeRingGlow", "Chopper/NukeRing", fresh, m => m.SetColor("_Colour", new Color(2.4f, 0.8f, 0.1f, 1f)));
            var ringWind = Mat("NukeRingWind", "Chopper/NukeRing", fresh, m =>
            {
                m.SetColor("_Colour", new Color(0.85f, 0.86f, 0.88f, 0.9f));
                m.SetFloat("_Streaks", 2f);
                m.SetFloat("_Thin", 6f);
                m.SetFloat("_Gaps", 0.45f);
                m.SetFloat("_Arcs", 9f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            });
            var scorch = Mat("NukeScorch", "Chopper/NukeScorch", fresh, _ => { });
            var shock = Mat("NukeShock", "Chopper/ShockShell", fresh, m =>
            {
                m.SetFloat("_Distort", 0.06f);
                m.SetColor("_Tint", Color.clear);
                m.SetFloat("_RimPower", 5f);
                m.SetFloat("_Fill", 0.02f);
            });
            var condensation = Mat("NukeCondensation", "Chopper/ShockShell", fresh, m =>
            {
                m.SetFloat("_Distort", 0.015f);
                m.SetColor("_Tint", new Color(0.9f, 0.92f, 0.95f, 1f));
                m.SetFloat("_RimPower", 2.2f);
                m.SetFloat("_Fill", 0.08f);
            });
            var flame = EffectsAssets.EnsureFlameMaterial();
            var core = FlameCopy(flame, Folder + "/NukeCore.mat", new Color(14f, 11f, 8f, 1f), fresh);
            var smoke = EffectsAssets.EnsureSmokeMaterial();
            var sparks = EffectsAssets.EnsureSparkMaterial();
            if (cloud == null || ringGlow == null || scorch == null || shock == null || flame == null || sparks == null)
            {
                Debug.LogWarning("Nuke blast: a material or shader is missing; prefab not built.");
                return null;
            }

            var capMesh = SaveMesh(CapMesh(), "NukeCap");
            var stemMesh = SaveMesh(StemMesh(), "NukeStem");
            var puffMesh = SaveMesh(PuffMesh(), "NukePuff");
            var ringMesh = SaveMesh(RingMesh(0.86f, 128), "NukeRing");
            var discMesh = SaveMesh(DiscMesh(), "NukeDisc");
            var sphere = SphereMesh();

            var root = new GameObject("NukeBlast");
            try
            {
                Part(root.transform, "Scorch", discMesh, scorch);
                Part(root.transform, "Stem", stemMesh, cloud);
                var rise = new GameObject("Rise").transform;
                rise.SetParent(root.transform, false);
                Part(rise, "Cap", capMesh, cloud);
                var rings = new GameObject("Rings").transform;
                rings.SetParent(rise, false);
                for (var i = 0; i < NukeBlastTimeline.RingCount; i++) Part(rings, $"Ring{i}", ringMesh, ringGlow);
                Core(rise, core);
                Condensation(rise, condensation, sphere);
                Embers(rise, sparks);
                CapFire(rise, flame);
                RimSmoke(rise, smoke, "CapRimSmoke", 62f, 22f, 0.4f, new Color(0.62f, 0.5f, 0.46f), new Color(0.5f, 0.42f, 0.4f));
                var puffs = new GameObject("Puffs").transform;
                puffs.SetParent(root.transform, false);
                for (var i = 0; i < NukeBlastTimeline.PuffCount; i++) Part(puffs, $"Puff{i}", puffMesh, cloud);
                var swirls = new GameObject("Swirls").transform;
                swirls.SetParent(root.transform, false);
                for (var i = 0; i < NukeBlastTimeline.SwirlCount; i++) Part(swirls, $"Swirl{i}", ringMesh, ringWind);
                Shock(root.transform, shock, sphere);
                Debris(root.transform, debris, puffMesh);
                RisingSparks(root.transform, sparks);
                BaseSurge(root.transform, smoke);
                BaseClouds(root.transform, smoke, flame);
                var baseRim = RimSmoke(root.transform, smoke, "BaseRimSmoke", 95f, 26f, 0.9f, new Color(0.6f, 0.5f, 0.42f), new Color(0.48f, 0.4f, 0.34f));
                if (baseRim != null) baseRim.transform.localPosition = new Vector3(0f, 12f, 0f);

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ---- particle parts (sizes in metres for the base radius) -------------------------------------------------

        /// <summary>0-1.5 s: the white-hot core inside the forming fireball.</summary>
        private static void Core(Transform parent, Material material)
        {
            var system = Make(parent, "Core", material, ParticleSystemSimulationSpace.Local, out var renderer);
            var main = system.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(70f, 110f);
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(system, 0f, 8);
            Shape(system, ParticleSystemShapeType.Sphere, 6f);
            Size(system, Curve((0f, 0.3f), (0.2f, 1f), (1f, 1.2f)));
            Colour(system, Gradient(
                new[] { (0f, Color.white), (0.3f, new Color(1f, 0.9f, 0.65f)), (1f, new Color(1f, 0.45f, 0.1f)) },
                new[] { (0f, 1f), (0.5f, 0.8f), (1f, 0f) }));
            FlameSheet(system);
            renderer.maxParticleSize = 4f;
        }

        /// <summary>0.1-1.8 s: the condensation (Wilson) cloud, a white shell flashing out round the fireball.</summary>
        private static void Condensation(Transform parent, Material material, Mesh sphere)
        {
            var system = Make(parent, "Condensation", material, ParticleSystemSimulationSpace.Local, out var renderer);
            var main = system.main;
            main.duration = 1f;
            main.startLifetime = 1.7f;
            main.startSize = 280f;
            main.startSpeed = 0f;
            Burst(system, 0.1f, 1);
            Shape(system, ParticleSystemShapeType.Sphere, 0.01f);
            Size(system, Curve((0f, 0.1f), (0.35f, 0.75f), (1f, 1f)));
            Colour(system, Gradient(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 0f), (0.12f, 0.9f), (0.5f, 0.4f), (1f, 0f) }));
            MeshShell(renderer, sphere);
        }

        /// <summary>0-6 s: the shock front: a bubble that bends the view, running out at about the speed of sound.</summary>
        private static void Shock(Transform parent, Material material, Mesh sphere)
        {
            var system = Make(parent, "Shock", material, ParticleSystemSimulationSpace.Local, out var renderer);
            var main = system.main;
            main.duration = 1f;
            main.startLifetime = 6f;
            main.startSize = 4000f;  // 2 km across after 6 s: about 330 m/s
            main.startSpeed = 0f;
            Burst(system, 0f, 1);
            Shape(system, ParticleSystemShapeType.Sphere, 0.01f);
            Size(system, Curve((0f, 0.01f), (1f, 1f)));
            Colour(system, Gradient(new[] { (0f, Color.white), (1f, Color.white) }, new[] { (0f, 1f), (0.5f, 0.6f), (1f, 0f) }));
            MeshShell(renderer, sphere);
        }

        /// <summary>0-8 s: embers thrown out of the fireball, then drifting down.</summary>
        private static void Embers(Transform parent, Material material)
        {
            var system = Make(parent, "Embers", material, ParticleSystemSimulationSpace.World, out var renderer);
            var main = system.main;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(25f, 90f);
            main.gravityModifier = 0.25f;
            main.startColor = new Color(1f, 0.7f, 0.3f, 1f);
            main.maxParticles = 500;
            Burst(system, 0.1f, 180);
            var emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(30f, Curve((0f, 1f), (1f, 0f)));
            Shape(system, ParticleSystemShapeType.Sphere, 40f);
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 8f;
            limit.dampen = 0.08f;
            Colour(system, Gradient(new[] { (0f, Color.white), (0.6f, new Color(1f, 0.55f, 0.2f)), (1f, new Color(0.6f, 0.15f, 0.05f)) },
                new[] { (0f, 1f), (0.8f, 0.9f), (1f, 0f) }));
            Noise(system, 6f, 0.3f);
            renderer.maxParticleSize = 0.2f;
        }

        /// <summary>0-12 s: fire boiling inside the cap, showing through and round its edge (owner: "hot").</summary>
        private static void CapFire(Transform parent, Material material)
        {
            var system = Make(parent, "CapFire", material, ParticleSystemSimulationSpace.Local, out var renderer);
            var main = system.main;
            main.duration = 11f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(28f, 48f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 300;
            var emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(45f, Curve((0f, 1f), (0.5f, 0.7f), (1f, 0.1f)));
            var shape = Shape(system, ParticleSystemShapeType.Sphere, 50f);
            shape.scale = new Vector3(1.2f, 0.7f, 1.2f);
            shape.radiusThickness = 0.3f;
            Size(system, Curve((0f, 0.5f), (0.4f, 1f), (1f, 1.3f)));
            Colour(system, Gradient(
                new[] { (0f, new Color(1f, 0.85f, 0.5f)), (0.4f, new Color(1f, 0.5f, 0.15f)), (1f, new Color(0.5f, 0.1f, 0.03f)) },
                new[] { (0f, 0f), (0.15f, 0.85f), (0.7f, 0.5f), (1f, 0f) }));
            Noise(system, 5f, 0.2f);
            FlameSheet(system);
            renderer.maxParticleSize = 4f;
        }

        /// <summary>
        /// Soft smoke round a rim (the cap's edge, the ring of base puffs): lit smoke cards on a flat ring that drift out
        /// and keep spreading, so the solid cloud's outline reads as volume (owner: "more volumetric on the outer rim").
        /// </summary>
        private static ParticleSystem RimSmoke(Transform parent, Material material, string name, float radius, float thickness,
            float delay, Color colourA, Color colourB)
        {
            if (material == null) return null;
            var system = Make(parent, name, material, ParticleSystemSimulationSpace.Local, out var renderer);
            var main = system.main;
            main.duration = 15f;
            main.startDelay = delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(30f, 55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colourA, colourB);
            main.maxParticles = 400;
            var emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(28f, Curve((0f, 1f), (0.7f, 0.8f), (1f, 0.2f)));
            var shape = Shape(system, ParticleSystemShapeType.Donut, radius);
            shape.donutRadius = thickness;
            shape.rotation = new Vector3(90f, 0f, 0f);
            // Out from the middle all the time: the rim keeps spreading (owner: "keep expanding outwards").
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.radial = new ParticleSystem.MinMaxCurve(4f, 9f);
            Size(system, Curve((0f, 0.5f), (0.4f, 1f), (1f, 1.5f)));
            Colour(system, Gradient(new[] { (0f, new Color(1f, 0.8f, 0.6f)), (0.3f, Color.white), (1f, Color.white) },
                new[] { (0f, 0f), (0.2f, 0.75f), (0.7f, 0.55f), (1f, 0f) }));
            Noise(system, 3f, 0.12f);
            Spin(system, 0.15f);
            SmokeSheet.Apply(system);
            renderer.maxParticleSize = 3f;
            return system;
        }

        /// <summary>
        /// 0.3-25 s: the ground cloud as soft volume (owner: "the clouds on the ground still seem like a shell"): dense,
        /// lit smoke billows on the ring of puffs, hot orange at first, that keep rolling outwards and up, plus fire glowing
        /// inside them for the first seconds. The solid puffs stay inside as ragged, glowing cores.
        /// </summary>
        private static void BaseClouds(Transform parent, Material smoke, Material flame)
        {
            if (smoke == null) return;
            var system = Make(parent, "BaseClouds", smoke, ParticleSystemSimulationSpace.World, out var renderer);
            system.transform.localPosition = new Vector3(0f, 10f, 0f);
            var main = system.main;
            main.duration = 12f;
            main.startDelay = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(13f, 19f);
            main.startSize = new ParticleSystem.MinMaxCurve(32f, 62f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.66f, 0.55f, 0.46f), new Color(0.52f, 0.44f, 0.38f));
            main.maxParticles = 700;
            Burst(system, 0f, 170);
            var emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(22f, Curve((0f, 1f), (0.6f, 0.6f), (1f, 0f)));
            var shape = Shape(system, ParticleSystemShapeType.Donut, 85f);
            shape.donutRadius = 38f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            // Keep rolling out and up for the whole blast (owner: "keep expanding outwards").
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(1f, 3.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.radial = new ParticleSystem.MinMaxCurve(3f, 7f);
            Size(system, Curve((0f, 0.45f), (0.25f, 1f), (1f, 1.7f)));
            Colour(system, Gradient(
                new[] { (0f, new Color(1f, 0.62f, 0.38f)), (0.15f, new Color(1f, 0.85f, 0.7f)), (0.4f, Color.white), (1f, new Color(0.85f, 0.85f, 0.88f)) },
                new[] { (0f, 0f), (0.06f, 0.92f), (0.65f, 0.75f), (1f, 0f) }));
            Noise(system, 3.5f, 0.1f);
            Spin(system, 0.12f);
            SmokeSheet.Apply(system);
            renderer.maxParticleSize = 3f;

            if (flame == null) return;
            var fire = Make(parent, "BaseCloudFire", flame, ParticleSystemSimulationSpace.World, out var fireRenderer);
            fire.transform.localPosition = new Vector3(0f, 8f, 0f);
            var fireMain = fire.main;
            fireMain.duration = 5f;
            fireMain.startDelay = 0.3f;
            fireMain.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
            fireMain.startSize = new ParticleSystem.MinMaxCurve(26f, 46f);
            fireMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            fireMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            fireMain.maxParticles = 300;
            var fireEmission = fire.emission;
            fireEmission.rateOverTime = new ParticleSystem.MinMaxCurve(60f, Curve((0f, 1f), (1f, 0f)));
            var fireShape = Shape(fire, ParticleSystemShapeType.Donut, 80f);
            fireShape.donutRadius = 30f;
            fireShape.rotation = new Vector3(90f, 0f, 0f);
            Size(fire, Curve((0f, 0.5f), (1f, 1.2f)));
            Colour(fire, Gradient(
                new[] { (0f, new Color(1f, 0.75f, 0.4f)), (0.5f, new Color(1f, 0.4f, 0.1f)), (1f, new Color(0.4f, 0.08f, 0.02f)) },
                new[] { (0f, 0f), (0.15f, 0.7f), (1f, 0f) }));
            Noise(fire, 4f, 0.2f);
            FlameSheet(fire);
            fireRenderer.maxParticleSize = 3f;
        }

        /// <summary>Screen-plane spin only (smoke cards stay upright).</summary>
        private static void Spin(ParticleSystem system, float radiansPerSecond)
        {
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
        }

        /// <summary>0.5-14 s: hot sparks drawn up the stem into the cap, swirling.</summary>
        private static void RisingSparks(Transform parent, Material material)
        {
            var system = Make(parent, "RisingSparks", material, ParticleSystemSimulationSpace.World, out var renderer);
            var main = system.main;
            main.duration = 13f;
            main.startDelay = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(30f, 65f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.45f, 0.12f));
            main.maxParticles = 1200;
            var emission = system.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(110f, Curve((0f, 1f), (0.6f, 0.7f), (1f, 0f)));
            // A half dome: sparks fly out up and to every side (owner: "a semicircle", not one way).
            var shape = Shape(system, ParticleSystemShapeType.Hemisphere, 22f);
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = 0f;
            velocity.y = 0f;
            velocity.z = 0f;
            // Every orbital axis in the same curve mode (Unity rejects mixed modes).
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);  // swirl round the stem, same on every side
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            Colour(system, Gradient(new[] { (0f, Color.white), (1f, new Color(1f, 0.4f, 0.1f)) },
                new[] { (0f, 0f), (0.1f, 1f), (0.8f, 0.8f), (1f, 0f) }));
            Noise(system, 10f, 0.25f);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 2f;
            renderer.maxParticleSize = 0.2f;
        }

        /// <summary>0.3-12 s: the base surge, a ring of dust driven out along the ground under the puffs.</summary>
        private static void BaseSurge(Transform parent, Material material)
        {
            if (material == null) return;
            var system = Make(parent, "BaseSurge", material, ParticleSystemSimulationSpace.World, out var renderer);
            var main = system.main;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(18f, 34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(70f, 120f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.6f, 0.5f, 0.42f, 1f);
            main.maxParticles = 500;
            Burst(system, 0.3f, 220);
            Burst(system, 0.55f, 140);
            var shape = Shape(system, ParticleSystemShapeType.Circle, 30f);
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radiusThickness = 0f;
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 4f;
            limit.dampen = 0.06f;
            Size(system, Curve((0f, 0.5f), (1f, 2.2f)));
            Colour(system, Gradient(new[] { (0f, new Color(1f, 0.75f, 0.55f)), (0.3f, Color.white), (1f, Color.white) },
                new[] { (0f, 0f), (0.08f, 0.8f), (0.6f, 0.45f), (1f, 0f) }));
            Noise(system, 2f, 0.15f);
            SmokeSheet.Apply(system);
            renderer.maxParticleSize = 3f;
        }

        /// <summary>0.15-7 s: dark chunks thrown up and out, falling back.</summary>
        private static void Debris(Transform parent, Material material, Mesh chunk)
        {
            var system = Make(parent, "Debris", material, ParticleSystemSimulationSpace.World, out var renderer);
            var main = system.main;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(40f, 110f);
            main.gravityModifier = 1f;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(system, 0.15f, 24);
            var shape = Shape(system, ParticleSystemShapeType.Cone, 15f);
            shape.angle = 55f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rotation.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rotation.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = chunk;
            renderer.alignment = ParticleSystemRenderSpace.Local;
        }

        // ---- meshes (unit size; the system scales them) ----------------------------------------------------------

        /// <summary>The mushroom cap: a lumpy, flattened ball with a belt round its middle and a narrow underside.</summary>
        private static Mesh CapMesh()
        {
            var random = new System.Random(0x4e75);
            var bumps = Bumps(random, 22, 0.35f, 0.6f, 0.08f, 0.2f);
            return Lumpy(4, random, dir =>
            {
                var r = 1f + Bump(dir, bumps) + 0.1f * Mathf.Exp(-Mathf.Pow((dir.y + 0.05f) / 0.16f, 2f));
                var p = dir * r;
                p.y *= 0.78f;
                // The underside pulls in toward the stem.
                if (p.y < -0.1f)
                {
                    var pull = Mathf.Lerp(1f, 0.5f, Mathf.Clamp01((-p.y - 0.1f) / 0.6f));
                    p.x *= pull;
                    p.z *= pull;
                }
                return p;
            }, "NukeCap");
        }

        /// <summary>A base puff: a lumpy cauliflower ball with a flat bottom.</summary>
        private static Mesh PuffMesh()
        {
            var random = new System.Random(0x5055);
            var bumps = Bumps(random, 12, 0.45f, 0.8f, 0.15f, 0.32f);
            return Lumpy(3, random, dir =>
            {
                var p = dir * (1f + Bump(dir, bumps));
                p.y = Mathf.Max(p.y * 0.85f, -0.3f);
                return p;
            }, "NukePuff");
        }

        /// <summary>The stem: a lathe from y 0 (ground, a wide flared foot) to y 1 (narrow neck under the cap), lumpy.</summary>
        private static Mesh StemMesh()
        {
            const int around = 32, up = 40;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (var j = 0; j <= up; j++)
            {
                var y = j / (float)up;
                var radius = 0.26f + 0.6f * Mathf.Exp(-y * 6f) + 0.1f * Mathf.Exp(-(1f - y) * 9f);
                for (var i = 0; i < around; i++)
                {
                    var a = i * Mathf.PI * 2f / around;
                    var lump = 1f + 0.22f * (Mathf.PerlinNoise(Mathf.Cos(a) * 1.6f + 10f + y * 2.5f, Mathf.Sin(a) * 1.6f + 20f + y * 4f) - 0.5f);
                    vertices.Add(new Vector3(Mathf.Cos(a) * radius * lump, y, Mathf.Sin(a) * radius * lump));
                }
            }
            for (var j = 0; j < up; j++)
            for (var i = 0; i < around; i++)
            {
                int a = j * around + i, b = j * around + (i + 1) % around, c = a + around, d = b + around;
                triangles.AddRange(new[] { a, c, b, b, c, d });
            }
            return Finish("NukeStem", vertices, triangles, null);
        }

        /// <summary>A flat ring in XZ: inner radius <paramref name="inner"/>, outer 1; uv.x round, uv.y inner→outer.</summary>
        private static Mesh RingMesh(float inner, int segments)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (var i = 0; i <= segments; i++)
            {
                var u = i / (float)segments;
                var a = u * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices.Add(dir * inner);
                uvs.Add(new Vector2(u, 0f));
                vertices.Add(dir);
                uvs.Add(new Vector2(u, 1f));
                if (i == segments) continue;
                var k = i * 2;
                triangles.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
            }
            // A short wall at the middle of the band, so the ring still shows when seen edge-on from a chopper.
            var start = vertices.Count;
            var middle = (inner + 1f) * 0.5f;
            var half = (1f - inner) * 0.3f;
            for (var i = 0; i <= segments; i++)
            {
                var u = i / (float)segments;
                var a = u * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * middle;
                vertices.Add(dir + Vector3.down * half);
                uvs.Add(new Vector2(u, 0f));
                vertices.Add(dir + Vector3.up * half);
                uvs.Add(new Vector2(u, 1f));
                if (i == segments) continue;
                var k = start + i * 2;
                triangles.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 });
            }
            return Finish("NukeRing", vertices, triangles, uvs);
        }

        /// <summary>A flat square from -1 to 1 in XZ, uv 0..1 (the burn mark).</summary>
        private static Mesh DiscMesh()
        {
            var vertices = new List<Vector3> { new(-1f, 0f, -1f), new(1f, 0f, -1f), new(-1f, 0f, 1f), new(1f, 0f, 1f) };
            var uvs = new List<Vector2> { new(0f, 0f), new(1f, 0f), new(0f, 1f), new(1f, 1f) };
            return Finish("NukeDisc", vertices, new List<int> { 0, 2, 1, 1, 2, 3 }, uvs);
        }

        private static (Vector3 Dir, float Size, float Height)[] Bumps(System.Random random, int count, float minSize, float maxSize,
            float minHeight, float maxHeight)
        {
            var bumps = new (Vector3, float, float)[count];
            for (var i = 0; i < count; i++)
            {
                var dir = new Vector3(Range(random, -1f, 1f), Range(random, -0.6f, 1f), Range(random, -1f, 1f)).normalized;
                bumps[i] = (dir, Range(random, minSize, maxSize), Range(random, minHeight, maxHeight));
            }
            return bumps;
        }

        /// <summary>Cauliflower lumps: the highest bump wins (rounded puffs), plus a little of the others.</summary>
        private static float Bump(Vector3 dir, (Vector3 Dir, float Size, float Height)[] bumps)
        {
            float best = 0f, sum = 0f;
            foreach (var (bumpDir, size, height) in bumps)
            {
                var angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, bumpDir), -1f, 1f));
                var t = Mathf.Clamp01(1f - angle / size);
                var h = height * Mathf.Sqrt(t * (2f - t));  // a round dome, not a cone
                best = Mathf.Max(best, h);
                sum += h;
            }
            return best + sum * 0.08f;
        }

        private static Mesh Lumpy(int subdivisions, System.Random random, Func<Vector3, Vector3> shape, string name)
        {
            Icosphere(subdivisions, out var directions, out var triangles);
            var vertices = directions.ConvertAll(d => shape(d));
            return Finish(name, vertices, triangles, null);
        }

        private static void Icosphere(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            vertices = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (var i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
            triangles = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var cache = new Dictionary<long, int>();
            var verts = vertices;
            int Middle(int a, int b)
            {
                var key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out var index)) return index;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            for (var s = 0; s < subdivisions; s++)
            {
                var next = new List<int>(triangles.Count * 4);
                for (var i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    int ab = Middle(a, b), bc = Middle(b, c), ca = Middle(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                triangles = next;
            }
        }

        private static Mesh Finish(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            System.IO.Directory.CreateDirectory(MeshFolder);
            var path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

        // ---- building blocks -------------------------------------------------------------------------------------

        private static void Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static ParticleSystem Make(Transform parent, string name, Material material, ParticleSystemSimulationSpace space,
            out ParticleSystemRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = space;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            return system;
        }

        private static void Burst(ParticleSystem system, float time, short count)
        {
            var emission = system.emission;
            var bursts = new ParticleSystem.Burst[emission.burstCount + 1];
            emission.GetBursts(bursts);
            bursts[^1] = new ParticleSystem.Burst(time, count);
            emission.SetBursts(bursts);
        }

        private static ParticleSystem.ShapeModule Shape(ParticleSystem system, ParticleSystemShapeType type, float radius)
        {
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            return shape;
        }

        private static void Size(ParticleSystem system, AnimationCurve curve)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void Colour(ParticleSystem system, Gradient gradient)
        {
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = gradient;
        }

        private static void Noise(ParticleSystem system, float strength, float frequency)
        {
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.2f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
        }

        /// <summary>The 4 x 4 fireball flipbook, played once over each card's life.</summary>
        private static void FlameSheet(ParticleSystem system)
        {
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            sheet.cycleCount = 1;
        }

        private static void MeshShell(ParticleSystemRenderer renderer, Mesh sphere)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = sphere;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sortMode = ParticleSystemSortMode.None;
        }

        private static AnimationCurve Curve(params (float Time, float Value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, value) in keys) curve.AddKey(time, value);
            return curve;
        }

        private static Gradient Gradient((float Time, Color Colour)[] colours, (float Time, float Alpha)[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(Array.ConvertAll(colours, c => new GradientColorKey(c.Colour, c.Time)),
                Array.ConvertAll(alphas, a => new GradientAlphaKey(a.Alpha, a.Time)));
            return gradient;
        }

        private static Mesh SphereMesh()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(probe);
            return mesh;
        }

        /// <summary>A material asset on a shader; made again when <paramref name="fresh"/> (menu rebuild).</summary>
        private static Material Mat(string name, string shaderName, bool fresh, Action<Material> setup)
        {
            var path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && !fresh) return existing;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Nuke blast: shader {shaderName} not found.");
                return existing;
            }
            var material = existing != null ? existing : new Material(shader) { name = name };
            if (existing != null) material.shader = shader;
            setup(material);
            if (existing == null) AssetDatabase.CreateAsset(material, path);
            else EditorUtility.SetDirty(material);
            return material;
        }

        private static Material FlameCopy(Material flame, string path, Color colour, bool fresh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if ((existing != null && !fresh) || flame == null) return existing;
            if (existing != null)
            {
                existing.CopyPropertiesFromMaterial(flame);
                existing.SetColor("_BaseColor", colour);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var material = new Material(flame) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            material.SetColor("_BaseColor", colour);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
