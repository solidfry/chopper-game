using Chopper.Simulation.Bots;
using UnityEditor;

namespace Chopper.Editor.Setup
{
    /// <summary>The default bot profile asset. Creates it when missing; never overwrites tuned values.</summary>
    internal static class BotAssets
    {
        public const string Folder = "Assets/Chopper/Data/Bots";
        public const string ProfilePath = Folder + "/BotProfile_Default.asset";

        public static BotProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BotProfile>(ProfilePath);
            if (profile != null) return profile;

            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Chopper/Data", "Bots");
            profile = UnityEngine.ScriptableObject.CreateInstance<BotProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
