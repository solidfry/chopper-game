using UnityEngine;
using UnityEngine.PostProcessing;
using System.IO;

namespace UnityEditor.PostProcessing
{
    public class PostProcessingFactory
    {
        [MenuItem("Assets/Create/Post-Processing Profile", priority = 201)]
        static void MenuCreatePostProcessingProfile()
        {
            // Chopper: EndNameEditAction is an error in Unity 6.6; create the asset in the selected folder instead.
            var folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            else if (!AssetDatabase.IsValidFolder(folder)) folder = Path.GetDirectoryName(folder);
            var profile = CreatePostProcessingProfileAtPath(AssetDatabase.GenerateUniqueAssetPath(folder + "/New Post-Processing Profile.asset"));
            ProjectWindowUtil.ShowCreatedAsset(profile);
        }

        internal static PostProcessingProfile CreatePostProcessingProfileAtPath(string path)
        {
            var profile = ScriptableObject.CreateInstance<PostProcessingProfile>();
            profile.name = Path.GetFileName(path);
            profile.fog.enabled = true;
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }
    }
}
