using System.Collections.Generic;
using System.IO;
using Chopper.Presentation.Config;
using Chopper.Simulation.Progression;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Progression setup: the <see cref="ProgressionSettings"/> asset (step 1). Never changes an asset that exists.
    /// Written without the editor (13 Nov 2026).
    /// </summary>
    internal static class ProgressionAssets
    {
        private const string Folder = "Assets/Chopper/Data/Progression";
        public const string SettingsPath = Folder + "/ProgressionSettings.asset";

        public static ProgressionSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ProgressionSettings>(SettingsPath);
            if (settings != null) return settings;

            Directory.CreateDirectory(Folder);
            settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>The account level of each paint skin that has one, for the spawner (the server's skin check).</summary>
        public static SkinUnlockRow[] SkinLevels(GameConfig config)
        {
            var rows = new List<SkinUnlockRow>();
            if (config != null && config.Skins != null)
                foreach (var skin in config.Skins.Skins)
                    if (skin != null && skin.Id != 0 && skin.UnlockLevel > 0)
                        rows.Add(new SkinUnlockRow { SkinId = skin.Id, Level = skin.UnlockLevel });
            return rows.ToArray();
        }
    }
}
