using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>
    /// A/B check for skins, in the editor or in play: put it on a kit model (or a parent of several) in any scene, pick a
    /// skin and Baked (one URP Lit material per skin) or Runtime (the skin shader, one material for all skins). It swaps
    /// the materials of every renderer under it whose material belongs to a kit texture set. With Flip every, play mode
    /// swaps Baked and Runtime on a timer. Not used by the game.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SkinPreview : MonoBehaviour
    {
        public enum Method { Baked, Runtime }

        [SerializeField] private SkinLibrary library;
        [SerializeField] private Method method = Method.Runtime;
        [SerializeField, Tooltip("Row in the skin library (0 = the kit's own look).")] private int skin;
        [SerializeField, Tooltip("Play mode: swap Baked and Runtime every this many seconds (0 = off).")]
        private float flipEvery;

        private MaterialPropertyBlock _block;
        private float _nextFlip;

        private void OnEnable() => Apply();

        private void OnValidate()
        {
#if UNITY_EDITOR
            // Renderers cannot change materials inside OnValidate.
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
#endif
        }

        private void Update()
        {
            if (!Application.isPlaying || flipEvery <= 0f || Time.time < _nextFlip) return;
            _nextFlip = Time.time + flipEvery;
            method = method == Method.Baked ? Method.Runtime : Method.Baked;
            Apply();
        }

        [ContextMenu("Next skin")]
        private void NextSkin()
        {
            if (library == null || library.Skins.Count == 0) return;
            skin = (skin + 1) % library.Skins.Count;
            Apply();
        }

        [ContextMenu("Swap Baked / Runtime")]
        private void Swap()
        {
            method = method == Method.Baked ? Method.Runtime : Method.Baked;
            Apply();
        }

        private void Apply()
        {
            if (library == null || library.Skins.Count == 0) return;
            skin = Mathf.Clamp(skin, 0, library.Skins.Count - 1);
            _block ??= new MaterialPropertyBlock();
            var entry = library.Skins[skin];
            foreach (var target in GetComponentsInChildren<Renderer>(true))
            {
                var materials = target.sharedMaterials;
                var runtime = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var set = library.SetOf(materials[i]);
                    if (set == null) continue;
                    var wanted = method == Method.Runtime ? library.RuntimeMaterial(set) : null;
                    if (method == Method.Baked) library.TryGetMaterial(entry.Id, set, out wanted);
                    if (wanted == null) continue;  // no runtime material yet (shader graph missing): leave it
                    materials[i] = wanted;
                    runtime |= method == Method.Runtime;
                }
                target.sharedMaterials = materials;
                if (runtime)
                {
                    target.GetPropertyBlock(_block);
                    _block.SetFloat(SkinLibrary.SkinIndexProperty, skin);
                    target.SetPropertyBlock(_block);
                }
                else
                {
                    target.SetPropertyBlock(null);
                }
            }
        }
    }
}
