using Chopper.Presentation.Effects;
using UnityEditor;
using UnityEngine;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// The weapon look library (Docs/WeaponFeelPlan.md) with its kind defaults. The defaults start empty: every slot
    /// falls back to the shared prefabs and cues, so the game looks and sounds as before. Families are added per step.
    /// Creates what is missing; never overwrites a look.
    /// </summary>
    internal static class WeaponLookAssets
    {
        private const string Folder = "Assets/Chopper/Data/Effects/WeaponLooks";
        private const string LibraryPath = Folder + "/WeaponLookLibrary.asset";

        public static WeaponLookLibrary Ensure()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var library = AssetDatabase.LoadAssetAtPath<WeaponLookLibrary>(LibraryPath);
            var created = library == null;
            if (created) library = ScriptableObject.CreateInstance<WeaponLookLibrary>();

            var gun = Look("Look_GunDefault");
            var rocket = Look("Look_RocketDefault");
            var guided = Look("Look_GuidedDefault");
            if (created)
            {
                library.Initialise(gun, rocket, guided);
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            else if (library.FillDefaults(gun, rocket, guided)) EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        private static WeaponLook Look(string assetName)
        {
            var path = $"{Folder}/{assetName}.asset";
            var look = AssetDatabase.LoadAssetAtPath<WeaponLook>(path);
            if (look != null) return look;
            look = ScriptableObject.CreateInstance<WeaponLook>();
            AssetDatabase.CreateAsset(look, path);
            return look;
        }
    }
}
