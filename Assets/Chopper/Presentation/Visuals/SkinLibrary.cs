using System;
using System.Collections.Generic;
using Chopper.Simulation.Config;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// The paint skins of one part kit (made by PartForge, <c>Tools/PartForge/partforge/skins.py</c>). Each skin has one
    /// URP Lit material per texture set: one set per chassis (named after it, e.g. "Gunship") and one shared "Modules"
    /// set. Setup step 5 fills it from the kit manifest; display names, descriptions and unlock levels can be edited here
    /// and are kept when the kit is imported again. Ids are saved in player data: never reuse or change one.
    /// </summary>
    [CreateAssetMenu(menuName = "Chopper/Visuals/Skin Library", fileName = "Skins")]
    public sealed class SkinLibrary : ScriptableObject, IValidatedAsset
    {
        public const string ModulesSet = "Modules";

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
            public SetMaterial[] Materials = Array.Empty<SetMaterial>();
        }

        [SerializeField] private string kit;
        [SerializeField, Tooltip("The first skin is the default (the kit's own look).")]
        private Skin[] skins = Array.Empty<Skin>();

        public string Kit => kit;
        public IReadOnlyList<Skin> Skins => skins;
        public Skin Default => skins.Length > 0 ? skins[0] : null;

        public Skin Find(ushort id)
        {
            foreach (var skin in skins)
                if (skin.Id == id) return skin;
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
        }

        /// <summary>Editor only: used by the kit import step. Keeps edited names, descriptions and unlock levels.</summary>
        public void Initialise(string kitName, Skin[] entries)
        {
            foreach (var entry in entries)
            {
                var old = Find(entry.Id);
                if (old == null) continue;
                entry.UnlockLevel = old.UnlockLevel;
                if (!string.IsNullOrWhiteSpace(old.DisplayName)) entry.DisplayName = old.DisplayName;
                if (!string.IsNullOrWhiteSpace(old.Description)) entry.Description = old.Description;
            }
            kit = kitName;
            skins = entries;
        }
    }
}
