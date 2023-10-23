using Events;
using UI.ScriptableObjects;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
// using Levels.ScriptableObjects;

namespace UI
{
    public class TransitionController : MonoBehaviour
    {
        // [SerializeField] private LevelData level;
        [SerializeField] TransitionData transitionData;
        
        private void Start() => transitionData.Init();
        
        private void OnEnable()
        {
            GameEvents.onSceneTransitionInEvent += TransitionIn;
            GameEvents.onSceneTransitionOutEvent += TransitionOut;
            SceneManager.sceneLoaded += TransitionIn;
        }
        private void OnDisable()
        {
            GameEvents.onSceneTransitionInEvent -= TransitionIn;
            GameEvents.onSceneTransitionOutEvent -= TransitionOut;
            SceneManager.sceneLoaded -= TransitionIn;
        }

        // // The mouse controls are for testing only
        // private void Update()
        // {
        //     TestTransition();
        // }

        void TransitionIn()
        {
            transitionData.Progress = 0;
            transitionData.isTransitioning = true;
            StartCoroutine(transitionData.TransitionInCoroutine());
        }

        void TransitionIn(Scene scene, LoadSceneMode mode)
        {
            transitionData.Progress = 0;
            transitionData.isTransitioning = true;
            StartCoroutine(transitionData.TransitionInCoroutine());
        }

        void TransitionOut()
        {
            transitionData.Progress = 1f;
            transitionData.isTransitioning = true;
            StartCoroutine(transitionData.TransitionOutCoroutine());
        }

        private void TestTransition()
        {
            if (!transitionData.testingControls) return;

            var mouse = Mouse.current;

            if (mouse.leftButton.wasPressedThisFrame && !transitionData.isTransitioning)
            {
                TransitionOut();
            }

            if (mouse.rightButton.wasPressedThisFrame && !transitionData.isTransitioning)
            {
                TransitionIn();
            }
        }
    }

}
