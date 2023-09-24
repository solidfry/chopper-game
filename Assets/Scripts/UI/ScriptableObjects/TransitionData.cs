using System.Collections;
using Enums;
using Events;
using UnityEngine;

namespace UI.ScriptableObjects
{
    [CreateAssetMenu(fileName = "TransitionData", menuName = "Transitions/New TransitionData", order = 0)]
    public class TransitionData : ScriptableObject
    {
        [Header("State")]
        [SerializeField] public TransitionState nextTransitionState;
        [SerializeField] private Color color;
        [SerializeField] public bool isTransitioning = false;

        [Header("Transition Settings")]
        [SerializeField] public Sprite transitionImage;
        [SerializeField] public float speed = 1f;
        [field: SerializeField] public float Progress { get; set; } = 1f;
        [SerializeField] public Material material;
        [SerializeField] public float transitionInDelay = 1f, transitionOutDelay = 0.25f;

        [SerializeField] private Sprite[] transitionImages;
        [SerializeField] private Color[] transitionColors;

        [Header("Testing Only")]
        [SerializeField] public bool testingControls = false;

        private static readonly int CutOff = Shader.PropertyToID("_CutOff");
        private static readonly int MainColor = Shader.PropertyToID("_MainColor");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

#if UNITY_EDITOR
        private void OnValidate()
        {
            nextTransitionState = TransitionState.In;
        }

#endif

        public void Init()
        {
            material.SetColor(MainColor, color);

            SetStateValues();

            if (transitionImage == null) return;
            int randomTexture = Random.Range(0, transitionImages.Length);
            int randomColor = Random.Range(0, transitionColors.Length);
            transitionImage = transitionImages[randomTexture];
            color = transitionColors[randomColor];
            material.SetColor(MainColor, color);
            material.SetTexture(MainTex, transitionImages[randomTexture].texture);
        }

        public void SetStateValues()
        {
            switch (nextTransitionState)
            {
                case TransitionState.In:
                    Progress = 0;
                    material.SetFloat(CutOff, Progress - 0.1f);
                    break;
                case TransitionState.Out:
                    Progress = 1;
                    material.SetFloat(CutOff, Progress + 0.1f);
                    break;
                default:
                    break;
            }
        }

        public IEnumerator TransitionInCoroutine(float initialDelaySeconds = 0f)
        {
            GameEvents.onSceneTransitionStartEvent?.Invoke();
            yield return new WaitForSeconds(initialDelaySeconds == 0 ? transitionInDelay : initialDelaySeconds);
            while (Progress < 1f)
            {
                Progress += Time.deltaTime * speed;
                SetCutOff();
                yield return null;
            }
            isTransitioning = false;
            GameEvents.onSceneTransitionEndEvent?.Invoke();
            nextTransitionState = TransitionState.Out;
        }

        public IEnumerator TransitionOutCoroutine(float initialDelaySeconds = 0f)
        {
            GameEvents.onSceneTransitionStartEvent?.Invoke();
            yield return new WaitForSeconds(initialDelaySeconds == 0 ? transitionInDelay : initialDelaySeconds);
            while (Progress > 0f)
            {
                Progress -= Time.deltaTime * speed;
                SetCutOff();
                yield return null;
            }
            isTransitioning = false;
            //We need to load the next level
            GameEvents.onSceneTransitionEndEvent?.Invoke();
            GameEvents.onLevelLoadEvent?.Invoke();
            nextTransitionState = TransitionState.In;
        }

        private void SetCutOff()
        {
            var transitionOffset = nextTransitionState == TransitionState.In ? 0.1f : -0.1f;
            material.SetFloat(CutOff, Progress + transitionOffset);
        }

    }
}