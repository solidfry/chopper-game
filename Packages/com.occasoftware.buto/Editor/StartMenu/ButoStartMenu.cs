using System;
using UnityEngine;
using UnityEditor;

namespace OccaSoftware.Buto.Editor
{
    public class ButoStartMenu : EditorWindow
    {
        // Source for UUID: https://shortunique.id/
        private static string modalIdKey = "Buto";
        private static string modalIdValue = "TAL0bX";

        private Texture2D logo;
        private GUIStyle header,
            button,
            contentSection;
        private GUILayoutOption[] contentLayoutOptions;
        private static bool listenToEditorUpdates;
        private static ButoStartMenu startMenu;

        [MenuItem("OccaSoftware/Buto/Start Menu")]
        public static void SetupMenu()
        {
            startMenu = CreateWindow();
            CenterWindowInEditor(startMenu);
            LoadLogo(startMenu);
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            RegisterModal();
        }

        void OnGUI()
        {
            SetupHeaderStyle(startMenu);
            SetupButtonStyle(startMenu);
            SetupContentSectionStyle(startMenu);

            GUILayout.BeginVertical();
            DrawHeader();
            DrawReviewRequest();
            DrawHelpLinks();
            DrawUpgradeLinks();
            GUILayout.EndVertical();
        }

        #region Setup
        private static ButoStartMenu CreateWindow()
        {
            ButoStartMenu startMenu = (ButoStartMenu)GetWindow(typeof(ButoStartMenu));
            startMenu.position = new Rect(0, 0, 300, 480);
            return startMenu;
        }

        private static void CenterWindowInEditor(EditorWindow startMenu)
        {
            Rect mainWindow = EditorGUIUtility.GetMainWindowPosition();
            Rect currentWindowPosition = startMenu.position;
            float centerX = (mainWindow.width - currentWindowPosition.width) * 0.5f;
            float centerY = (mainWindow.height - currentWindowPosition.height) * 0.5f;
            currentWindowPosition.x = mainWindow.x + centerX;
            currentWindowPosition.y = mainWindow.y + centerY;
            startMenu.position = currentWindowPosition;
        }

        private static void LoadLogo(ButoStartMenu startMenu)
        {
            startMenu.logo = (Texture2D)
                AssetDatabase.LoadAssetAtPath(
                    "Packages/com.occasoftware.buto/Editor/StartMenu/Logo.png",
                    typeof(Texture2D)
                );
        }

        private static void SetupHeaderStyle(ButoStartMenu startMenu)
        {
            startMenu.header = new GUIStyle(EditorStyles.boldLabel);
            startMenu.header.fontSize = 18;
            startMenu.header.wordWrap = true;
            startMenu.header.padding = new RectOffset(0, 0, 0, 0);
        }

        private static void SetupButtonStyle(ButoStartMenu startMenu)
        {
            startMenu.button = new GUIStyle("button");
            startMenu.button.fontSize = 18;
            startMenu.button.fontStyle = FontStyle.Bold;
            startMenu.button.fixedHeight = 40;
        }

        private static void SetupContentSectionStyle(ButoStartMenu startMenu)
        {
            startMenu.contentSection = new GUIStyle("label");
            startMenu.contentSection.margin = new RectOffset(16, 16, 16, 16);
            startMenu.contentSection.padding = new RectOffset(0, 0, 0, 0);
            startMenu.contentLayoutOptions = new GUILayoutOption[] { GUILayout.Width(600) };
        }
        #endregion


        #region Modal Handler
        private static void RegisterModal()
        {
            if (!listenToEditorUpdates && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                listenToEditorUpdates = true;
                EditorApplication.update += PopModal;
            }
        }

        private static void PopModal()
        {
            EditorApplication.update -= PopModal;
            string storedModalIdValue = EditorPrefs.GetString(modalIdKey, "");
            if (string.IsNullOrEmpty(storedModalIdValue) || storedModalIdValue != modalIdValue)
            {
                EditorPrefs.SetString(modalIdKey, modalIdValue);
                SetupMenu();
            }
        }
        #endregion



        #region UI Drawer
        private void DrawHeader()
        {
            GUILayout.BeginVertical(contentSection, contentLayoutOptions);
            GUIStyle logoStyle = new GUIStyle("label");
            GUILayoutOption[] logoOptions = new GUILayoutOption[] { GUILayout.Width(230) };
            logoStyle.padding = new RectOffset(0, 0, 0, 0);
            logoStyle.margin = new RectOffset(0, 0, 0, 0);
            logoStyle.alignment = TextAnchor.MiddleCenter;
            GUILayout.Label(logo, logoStyle, logoOptions);
            GUILayout.EndVertical();
        }

        private void DrawReviewRequest()
        {
            GUILayout.BeginVertical(contentSection, contentLayoutOptions);

            GUILayout.Label("Enjoying Buto?", header);

            if (EditorGUILayout.LinkButton("Click here to rate it on the Unity Asset Store."))
            {
                Application.OpenURL("https://assetstore.unity.com/packages/slug/213497");
            }

            GUILayout.EndVertical();
        }

        private void DrawHelpLinks()
        {
            GUILayout.BeginVertical(contentSection, contentLayoutOptions);
            GUILayout.Label("I am here to help.", header);
            if (EditorGUILayout.LinkButton("Product Page"))
            {
                Application.OpenURL("https://www.occasoftware.com/assets/buto");
            }

            if (EditorGUILayout.LinkButton("Usage Manual"))
            {
                Application.OpenURL("https://docs.occasoftware.com/buto");
            }

            if (EditorGUILayout.LinkButton("Discord"))
            {
                Application.OpenURL("https://www.occasoftware.com/discord");
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawUpgradeLinks()
        {
            GUILayout.BeginVertical(contentSection, contentLayoutOptions);

            GUILayout.Label("Next steps.", header);

            if (EditorGUILayout.LinkButton("Get the OccaSoftware Bundle"))
            {
                Application.OpenURL("https://occasoftware.com/occasoftware-bundle");
            }
            if (EditorGUILayout.LinkButton("Join my Newsletter"))
            {
                Application.OpenURL("https://www.occasoftware.com/newsletter");
            }
            EditorGUILayout.EndVertical();
        }
        #endregion
    }
}
