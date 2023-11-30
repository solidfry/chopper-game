using UnityEditor;
using UnityEngine;

namespace Editor
{
    public class GuidToAssetPath : EditorWindow
    {
        string guid = "";
        string path = "";
        [MenuItem("Tools/GUID Finder")]
        static void CreateWindow()
        {
            GuidToAssetPath window = (GuidToAssetPath)EditorWindow.GetWindowWithRect(typeof(GuidToAssetPath), new Rect(0, 0, 400, 120));
        }
 
        void OnGUI()
        {
            GUILayout.Label("Enter guid");
            guid = GUILayout.TextField(guid);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Get Asset Path",GUILayout.Width(120)))
                path = GetAssetPath(guid);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Abort", GUILayout.Width(120)))
                Close();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(path);
        }
        static string GetAssetPath(string guid)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            Debug.Log(p);
            if (p.Length == 0) p = "not found";
            return p;
        }
    }
}