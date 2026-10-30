using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// The paint skins of one part kit (made by PartForge, <c>Tools/PartForge/partforge/skins.py</c>). Texture sets: one
    /// per chassis (named after it, e.g. "Gunship"), one shared "Modules" set and one shared "Cockpits" set. Two ways to show a skin (A/B):
    /// <list type="bullet">
    /// <item><b>Baked:</b> one URP Lit material per set and skin (<see cref="Skin.Materials"/>).</item>
    /// <item><b>Runtime:</b> one skin-shader material per set (<see cref="RuntimeMaterials"/>) for every skin; the skin is
    /// the row <see cref="IndexOf"/> of <see cref="Table"/>, set per renderer as <see cref="SkinIndexProperty"/>. A new
    /// skin is data only: add an entry, edit its <see cref="Skin.Look"/>, press Setup step 5 (rebuilds the table).</item>
    /// </list>
    /// Setup step 5 adds the kit's new skins from the manifest; names, descriptions, unlock levels and looks edited here
    /// are kept. Ids are saved in player data: never reuse or change one.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Visuals/Skin Library", fileName = "Skins")]
    public sealed class SkinLibrary : ScriptableObject, IValidatedAsset
    {
        public const string ModulesSet = "Modules";
        /// <summary>Skin shader property: the skin table row (float, per renderer or per entity).</summary>
        public const string SkinIndexProperty = "_SkinIndex";

        [Serializable]
        public struct SetMaterial
        {
            public string Set;
            public Material Material;
        }

        [Serializable]
        public sealed class Skin
        {
            [Tooltip("Unique, never 0, never reused. Saved in player data.")]
            public ushort Id;
            [Tooltip("PartForge key (file names).")]
            public string Key;
            public string DisplayName;
            [TextArea] public string Description;
            public int UnlockLevel;
            [Tooltip("Baked: one material per texture set.")]
            public SetMaterial[] Materials = Array.Empty<SetMaterial>();
            [Tooltip("Runtime: the skin shader's parameters (a row of the skin table).")]
            public SkinLook Look = new();
        }

        [SerializeField] private string kit;
        [SerializeField, Tooltip("The first skin is the default (the kit's own look).")]
        private Skin[] skins = Array.Empty<Skin>();
        [SerializeField, Tooltip("Runtime: one skin-shader material per texture set (all skins).")]
        private SetMaterial[] runtimeMaterials = Array.Empty<SetMaterial>();
        [SerializeField, Tooltip("Runtime: one row per skin (built by Setup step 5 from the looks).")]
        private Texture2D table;

        public string Kit => kit;
        public IReadOnlyList<Skin> Skins => skins;
        public Skin Default => skins.Length > 0 ? skins[0] : null;
        public IReadOnlyList<SetMaterial> RuntimeMaterials => runtimeMaterials;
        public Texture2D Table => table;

        public Skin Find(ushort id)
        {
            foreach (var skin in skins)
                if (skin.Id == id) return skin;
            return null;
        }

        /// <summary>The skin's table row (0 = the default skin for unknown ids).</summary>
        public int IndexOf(ushort id)
        {
            for (var i = 0; i < skins.Length; i++)
                if (skins[i].Id == id) return i;
            return 0;
        }

        /// <summary>The texture set a material belongs to (any skin's baked material or a runtime material), or null.</summary>
        public string SetOf(Material material)
        {
            if (material == null) return null;
            foreach (var entry in runtimeMaterials)
                if (entry.Material == material) return entry.Set;
            foreach (var skin in skins)
            foreach (var entry in skin.Materials)
                if (entry.Material == material) return entry.Set;
            return null;
        }

        public Material RuntimeMaterial(string set)
        {
            foreach (var entry in runtimeMaterials)
                if (entry.Set == set) return entry.Material;
            return null;
        }

        /// <summary>The material of a skin for one texture set (a chassis name or <see cref="ModulesSet"/>). Unknown skin
        /// ids fall back to the default skin.</summary>
        public bool TryGetMaterial(ushort skinId, string set, out Material material)
        {
            var skin = Find(skinId) ?? Default;
            material = null;
            if (skin == null) return false;
            foreach (var entry in skin.Materials)
            {
                if (entry.Set != set) continue;
                material = entry.Material;
                return material != null;
            }
            return false;
        }

        public void CollectProblems(List<string> problems)
        {
            if (skins.Length == 0) problems.Add($"{name}: no skins. Press '5. Import part kits'.");
            var ids = new HashSet<ushort>();
            foreach (var skin in skins)
            {
                if (skin.Id == 0) problems.Add($"{name}: skin '{skin.Key}' has id 0.");
                else if (!ids.Add(skin.Id)) problems.Add($"{name}: skin id {skin.Id} is used twice.");
                foreach (var entry in skin.Materials)
                    if (entry.Material == null) problems.Add($"{name}: skin '{skin.Key}' has no material for set '{entry.Set}'.");
            }
            if (runtimeMaterials.Length == 0)
                problems.Add($"{name}: no runtime skin materials (make the ChopperSkin shader graph, then press '5. Import part kits').");
            if (table == null || table.height != Mathf.Max(skins.Length, 1) || table.width != SkinTable.Width)
                problems.Add($"{name}: skin table missing or out of date. Press '5. Import part kits'.");
        }

        /// <summary>Editor only: used by the kit import step.</summary>
        public void SetRuntime(SetMaterial[] materials, Texture2D skinTable)
        {
            runtimeMaterials = materials ?? Array.Empty<SetMaterial>();
            table = skinTable;
        }

        /// <summary>Editor only: used by the kit import step. The kit's skins in kit order, then skins added here by hand;
        /// a skin already here keeps its name, description, unlock level and look (only its baked materials are new).</summary>
        public void Initialise(string kitName, Skin[] entries)
        {
            var merged = new List<Skin>();
            foreach (var entry in entries)
            {
                var old = Find(entry.Id);
                if (old != null)
                {
                    entry.UnlockLevel = old.UnlockLevel;
                    if (!string.IsNullOrWhiteSpace(old.DisplayName)) entry.DisplayName = old.DisplayName;
                    if (!string.IsNullOrWhiteSpace(old.Description)) entry.Description = old.Description;
                    if (old.Look is { Filled: true }) entry.Look = old.Look;
                }
                merged.Add(entry);
            }
            foreach (var skin in skins)
            {
                var fromKit = false;
                foreach (var entry in entries) fromKit |= entry.Id == skin.Id;
                if (!fromKit) merged.Add(skin);
            }
            kit = kitName;
            skins = merged.ToArray();
        }

        /// <summary>The looks in table order (row = index).</summary>
        public SkinLook[] Looks()
        {
            var looks = new SkinLook[skins.Length];
            for (var i = 0; i < skins.Length; i++) looks[i] = skins[i].Look ?? new SkinLook();
            return looks;
        }
    }
}
