using System;
using System.IO;
using Chopper.Presentation.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace Chopper.Tests
{
    /// <summary>The C# skin table encoder must match PartForge's (runtime.encode), which the kit manifest carries.</summary>
    public sealed class SkinTableTests
    {
        private const string ManifestPath = "Assets/Chopper/Art/Kits/Kit02/Kit02.manifest.json";

        [Serializable]
        private sealed class Manifest
        {
            public Skin[] skins;
        }

        [Serializable]
        private sealed class Skin
        {
            public string key;
            public SkinParams runtime;
            public float[] tableFlat;
        }

        [Test]
        public void EncoderMatchesTheKitManifest()
        {
            if (!File.Exists(ManifestPath)) Assert.Ignore($"No kit manifest at {ManifestPath}.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            Assert.IsNotEmpty(manifest.skins);
            foreach (var skin in manifest.skins)
            {
                Assert.IsNotNull(skin.runtime, $"{skin.key}: no runtime values (old manifest: export the kit again).");
                var row = SkinTable.Encode(skin.runtime.ToLook());
                Assert.AreEqual(SkinTable.Width * 4, skin.tableFlat.Length, skin.key);
                for (var i = 0; i < SkinTable.Width; i++)
                for (var c = 0; c < 4; c++)
                    Assert.AreEqual(skin.tableFlat[i * 4 + c], row[i][c], 2e-3f, $"{skin.key}: texel {i}, channel {c}");
            }
        }

        [Test]
        public void CoverThresholdRunsFromHighToLow()
        {
            Assert.Greater(SkinTable.CoverThreshold(0.1f), SkinTable.CoverThreshold(0.5f));
            Assert.AreEqual(0.5005f, SkinTable.CoverThreshold(0.5f), 1e-4f);
        }
    }
}
