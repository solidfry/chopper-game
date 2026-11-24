using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Lens flares (owner, 24 Nov: "we should also add lens flare"), URP's SRP lens flare data built in code from the
    /// built-in shapes (no textures): a subtle sun flare (camera settings; put on the scene's sun at runtime by
    /// SpeedEffects) and a big orange nuke flare with an anamorphic streak (combat effects settings; on the nuke's lights).
    /// Rebuilt when missing, or from the menu Chopper > Tools > Rebuild Lens Flares.
    /// </summary>
    internal static class LensFlareAssets
    {
        public const string SunPath = "Assets/Chopper/Data/Effects/SunFlare.asset";
        public const string NukePath = "Assets/Chopper/Data/Effects/NukeFlare.asset";

        public static LensFlareDataSRP EnsureSun() => Load(SunPath) ?? Build(SunPath, Sun());
        public static LensFlareDataSRP EnsureNuke() => Load(NukePath) ?? Build(NukePath, Nuke());

        [MenuItem("Chopper/Tools/Rebuild Lens Flares")]
        private static void Rebuild()
        {
            Build(SunPath, Sun());
            Build(NukePath, Nuke());
            Debug.Log("Lens flares rebuilt.");
        }

        private static LensFlareDataSRP Load(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(path);
            return data != null ? data : null;
        }

        private static LensFlareDataSRP Build(string path, LensFlareDataElementSRP[] elements)
        {
            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(path);
            var created = data == null;
            if (created) data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            data.elements = elements;
            if (created) AssetDatabase.CreateAsset(data, path);
            else EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>Soft glow, a faint streak and a few dim ghosts across the view.</summary>
        private static LensFlareDataElementSRP[] Sun() => new[]
        {
            Glow(new Color(1f, 0.95f, 0.85f, 0.5f), 0.35f, 6f),
            Streak(new Color(1f, 0.9f, 0.75f, 0.5f), 0.12f, 4f, 0.08f),
            Ghosts(new Color(0.6f, 0.85f, 1f, 0.5f), 0.025f, 0.45f, 0.4f, 4, 1),
            Ghosts(new Color(1f, 0.7f, 0.5f, 0.5f), 0.02f, -0.35f, 0.6f, 3, 2),
            Hexagon(new Color(0.5f, 0.7f, 1f, 0.5f), 0.015f, 0.8f, 1.4f),
        };

        /// <summary>Big orange glow, a wide anamorphic streak, a halo ring and warm ghosts.</summary>
        private static LensFlareDataElementSRP[] Nuke() => new[]
        {
            Glow(new Color(1f, 0.8f, 0.55f, 0.5f), 1f, 14f),
            Glow(new Color(1f, 0.45f, 0.15f, 0.5f), 0.5f, 28f),
            Streak(new Color(1f, 0.6f, 0.3f, 0.5f), 0.7f, 10f, 0.06f),
            Halo(new Color(1f, 0.55f, 0.25f, 0.5f), 0.12f, 9f),
            Ghosts(new Color(1f, 0.6f, 0.3f, 0.5f), 0.08f, 0.4f, 0.6f, 5, 3),
            Ghosts(new Color(1f, 0.85f, 0.5f, 0.5f), 0.05f, -0.3f, 0.9f, 3, 4),
            Hexagon(new Color(1f, 0.5f, 0.2f, 0.5f), 0.04f, 0.7f, 2f),
        };

        private static LensFlareDataElementSRP Base(SRPLensFlareType type, Color tint, float intensity, float scale)
        {
            return new LensFlareDataElementSRP
            {
                flareType = type,
                tint = tint,
                localIntensity = intensity,
                uniformScale = scale,
                sizeXY = Vector2.one,
                blendMode = SRPLensFlareBlendMode.Additive,
                modulateByLightColor = false,
                visible = true,
                edgeOffset = 0.6f,
                fallOff = 1f,
            };
        }

        private static LensFlareDataElementSRP Glow(Color tint, float intensity, float scale)
        {
            var e = Base(SRPLensFlareType.Circle, tint, intensity, scale);
            e.edgeOffset = 1f;
            e.fallOff = 0.4f;
            return e;
        }

        private static LensFlareDataElementSRP Streak(Color tint, float intensity, float scale, float height)
        {
            var e = Base(SRPLensFlareType.Circle, tint, intensity, scale);
            e.sizeXY = new Vector2(12f, height);
            e.preserveAspectRatio = false;
            e.edgeOffset = 1f;
            e.fallOff = 0.6f;
            return e;
        }

        private static LensFlareDataElementSRP Halo(Color tint, float intensity, float scale)
        {
            var e = Base(SRPLensFlareType.Ring, tint, intensity, scale);
            e.ringThickness = 0.08f;
            e.edgeOffset = 0.3f;
            return e;
        }

        private static LensFlareDataElementSRP Hexagon(Color tint, float intensity, float position, float scale)
        {
            var e = Base(SRPLensFlareType.Polygon, tint, intensity, scale);
            e.position = position;
            e.sideCount = 6;
            e.sdfRoundness = 0.3f;
            return e;
        }

        private static LensFlareDataElementSRP Ghosts(Color tint, float intensity, float position, float scale, int count, int seed)
        {
            var e = Base(SRPLensFlareType.Circle, tint, intensity, scale);
            e.position = position;
            e.allowMultipleElement = true;
            e.count = count;
            e.distribution = SRPLensFlareDistribution.Random;
            e.lengthSpread = 1.2f;
            e.seed = seed;
            e.scaleVariation = 0.6f;
            e.positionVariation = new Vector2(0.4f, 0f);
            return e;
        }
    }
}
