using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chopper.Editor.Tools
{
    /// <summary>
    /// Chopper > Scene Jumper (Ctrl+Shift+J): every project scene in one list, to open, add or play with one click.
    /// Groups: favourites, recent, build scenes (build order), maps, other, subscenes (folded). Click a name to open
    /// (asks to save first), "+" to add it to the open scenes, "▶" to play from it without leaving the scene you are
    /// in. The play-mode start scene (top) makes every Play start there (e.g. always from the Hangar).
    /// Favourites, recent and folds are saved per project in EditorPrefs.
    /// </summary>
    public sealed class SceneJumper : EditorWindow
    {
        private const int RecentCount = 6;
        private static readonly string[] SkipFolders = { "Assets/AssetBundles", "Assets/ThirdParty", "Assets/Plugins", "Assets/Samples" };

        private readonly List<string> _all = new();
        private readonly HashSet<string> _build = new();
        private string _search = "";
        private Vector2 _scroll;

        [MenuItem("Chopper/Scene Jumper %#j", priority = 1)]
        private static void Open() => GetWindow<SceneJumper>("Scenes").Show();

        private static string Key(string name) => $"Chopper.SceneJumper.{PlayerSettings.productGUID}.{name}";
        private static List<string> LoadList(string name) =>
            EditorPrefs.GetString(Key(name), "").Split('|').Where(p => p.Length > 0).ToList();
        private static void SaveList(string name, IEnumerable<string> paths) => EditorPrefs.SetString(Key(name), string.Join("|", paths));

        private void OnEnable()
        {
            Refresh();
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.projectChanged += Refresh;
        }

        private void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorApplication.projectChanged -= Refresh;
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (string.IsNullOrEmpty(scene.path)) return;
            var recent = LoadList("Recent");
            recent.Remove(scene.path);
            recent.Insert(0, scene.path);
            SaveList("Recent", recent.Take(RecentCount));
            Repaint();
        }

        private void Refresh()
        {
            _all.Clear();
            _build.Clear();
            foreach (var scene in EditorBuildSettings.scenes) _build.Add(scene.path);
            foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (SkipFolders.Any(f => path.StartsWith(f + "/"))) continue;
                _all.Add(path);
            }
            _all.Sort((a, b) => string.CompareOrdinal(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b)));
            Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(56))) Refresh();
            }
            DrawStartScene();

            var favourites = LoadList("Favourites").Where(_all.Contains).ToList();
            var recent = LoadList("Recent").Where(_all.Contains).ToList();
            var buildOrder = EditorBuildSettings.scenes.Select(s => s.path).Where(_all.Contains).ToList();
            var rest = _all.Where(p => !_build.Contains(p)).ToList();
            var maps = rest.Where(p => Path.GetFileName(p).StartsWith("Map_") && !IsSubscene(p)).ToList();
            var subscenes = rest.Where(IsSubscene).ToList();
            var other = rest.Except(maps).Except(subscenes).ToList();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_search.Length > 0)
            {
                Group("Search", _all.Where(p => Path.GetFileNameWithoutExtension(p).IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0),
                    favourites, alwaysOpen: true);
            }
            else
            {
                Group("Favourites", favourites, favourites, alwaysOpen: true);
                Group("Recent", recent, favourites);
                Group("Build scenes", buildOrder, favourites);
                Group("Maps", maps, favourites);
                Group("Other", other, favourites);
                Group("Subscenes", subscenes, favourites, defaultOpen: false);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>A scene inside a folder named after another scene next to it (Map_City04/Map_City04_Gameplay).</summary>
        private bool IsSubscene(string path)
        {
            var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            return folder != null && _all.Contains(folder + ".unity");
        }

        private void DrawStartScene()
        {
            var names = new List<string> { "Play from: the open scene" };
            names.AddRange(_build.Where(_all.Contains).Select(p => "Play from: " + Path.GetFileNameWithoutExtension(p)));
            var paths = new List<string> { null };
            paths.AddRange(_build.Where(_all.Contains));

            var current = EditorSceneManager.playModeStartScene != null ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : null;
            var index = Mathf.Max(0, paths.IndexOf(current));
            var chosen = EditorGUILayout.Popup(index, names.ToArray());
            if (chosen == index) return;
            EditorSceneManager.playModeStartScene = paths[chosen] == null ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(paths[chosen]);
            EditorPrefs.SetString(Key("StartScene"), paths[chosen] ?? "");
        }

        private void Group(string title, IEnumerable<string> paths, List<string> favourites, bool alwaysOpen = false, bool defaultOpen = true)
        {
            var list = paths.ToList();
            if (list.Count == 0) return;
            var foldKey = Key("Fold." + title);
            var open = alwaysOpen || EditorPrefs.GetBool(foldKey, defaultOpen);
            if (!alwaysOpen)
            {
                var now = EditorGUILayout.Foldout(open, $"{title} ({list.Count})", true, EditorStyles.foldoutHeader);
                if (now != open) EditorPrefs.SetBool(foldKey, now);
                open = now;
            }
            else EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (!open) return;

            foreach (var path in list) Row(path, favourites);
            EditorGUILayout.Space(4);
        }

        private void Row(string path, List<string> favourites)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var active = SceneManager.GetActiveScene().path == path;
            var loaded = SceneManager.GetSceneByPath(path).isLoaded;
            using (new EditorGUILayout.HorizontalScope())
            {
                var star = favourites.Contains(path);
                if (GUILayout.Button(star ? "★" : "☆", EditorStyles.label, GUILayout.Width(16)))
                {
                    if (star) favourites.Remove(path);
                    else favourites.Add(path);
                    SaveList("Favourites", favourites);
                }

                var style = new GUIStyle(EditorStyles.label) { fontStyle = active ? FontStyle.Bold : loaded ? FontStyle.Italic : FontStyle.Normal };
                if (GUILayout.Button(new GUIContent(name, path), style)) OpenScene(path, OpenSceneMode.Single);
                if (GUILayout.Button(new GUIContent("+", "Add to the open scenes"), EditorStyles.miniButtonLeft, GUILayout.Width(22)))
                    OpenScene(path, OpenSceneMode.Additive);
                if (GUILayout.Button(new GUIContent("▶", "Play from this scene"), EditorStyles.miniButtonMid, GUILayout.Width(22)))
                    PlayFrom(path);
                if (GUILayout.Button(new GUIContent("◎", "Show in the Project window"), EditorStyles.miniButtonRight, GUILayout.Width(22)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
            }
        }

        private static void OpenScene(string path, OpenSceneMode mode)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Scene Jumper: stop play mode first.");
                return;
            }
            if (mode == OpenSceneMode.Single && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path, mode);
        }

        /// <summary>Plays from the scene once, then puts the start-scene choice back (see <see cref="PlayFromRestore"/>).</summary>
        private static void PlayFrom(string path)
        {
            if (EditorApplication.isPlaying) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetBool(PlayFromRestore.Pending, true);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Keeps the chosen play-mode start scene across domain reloads, and after a one-off "▶" play puts the
        /// chosen one back. State lives in SessionState and EditorPrefs, not in static fields.
        /// </summary>
        [InitializeOnLoad]
        private static class PlayFromRestore
        {
            public const string Pending = "Chopper.SceneJumper.PlayFromPending";

            static PlayFromRestore()
            {
                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool(Pending, false)) ApplySaved();
            }

            private static void OnPlayModeChanged(PlayModeStateChange change)
            {
                if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Pending, false)) return;
                SessionState.SetBool(Pending, false);
                ApplySaved();
            }

            private static void ApplySaved()
            {
                var saved = EditorPrefs.GetString(Key("StartScene"), "");
                EditorSceneManager.playModeStartScene = saved.Length > 0 ? AssetDatabase.LoadAssetAtPath<SceneAsset>(saved) : null;
            }
        }
    }
}
