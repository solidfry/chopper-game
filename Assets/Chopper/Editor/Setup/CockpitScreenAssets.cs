using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;

namespace Chopper.Editor.Setup
{
    /// <summary>
    /// Live cockpit screens: the render texture the screen pages are drawn into (the kit's screen material reads it),
    /// and the panel settings that draw the cockpit screens layout into it. Pages are square, side by side, in the order
    /// of PartForge's <c>palette.SCREEN_PAGES</c> (Flight, Tactical, Systems); the cockpit models' screen UVs pick them.
    /// </summary>
    internal static class CockpitScreenAssets
    {
        public const int PageSize = 512;
        public const int PageCount = 3;
        private const string Folder = "Assets/Chopper/UI/Cockpit";
        private const string TexturePath = Folder + "/CockpitScreens.renderTexture";
        private const string PanelPath = Folder + "/CockpitScreensPanel.asset";
        public const string LayoutPath = Folder + "/CockpitScreens.uxml";

        public static VisualTreeAsset Layout => AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);

        public static RenderTexture EnsureTexture()
        {
            var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(TexturePath);
            if (texture != null) return texture;
            // Depth with stencil: UI Toolkit masks (overflow, rounded clips) need it.
            texture = new RenderTexture(PageSize * PageCount, PageSize, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                name = Path.GetFileNameWithoutExtension(TexturePath),
                useMipMap = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Directory.CreateDirectory(Folder);
            AssetDatabase.CreateAsset(texture, TexturePath);
            return texture;
        }

        /// <param name="shared">The game's panel settings: the screens use its theme.</param>
        public static PanelSettings EnsurePanel(PanelSettings shared)
        {
            var texture = EnsureTexture();
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                Directory.CreateDirectory(Folder);
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            if (shared != null) panel.themeStyleSheet = shared.themeStyleSheet;
            panel.targetTexture = texture;
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = 1f;
            panel.clearColor = true;
            panel.colorClearValue = Color.clear;
            EditorUtility.SetDirty(panel);
            return panel;
        }
    }
}
