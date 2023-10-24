using System.Collections.Generic;
using Enums;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace UI
{
    public class NavigationManager : SingletonPersistent<NavigationManager>
    {
        [FormerlySerializedAs("_canvas")] [SerializeField] Canvas canvas;
    
        [SerializeField] List<NavButtonHandler> navItems;
        [SerializeField] List<Scenes> scenes;
        

        private void Start()
        {
            if(canvas == null) 
                canvas = GetComponent<Canvas>();


            SubscribeButtonsOnClick();
        }
        
        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        
        private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            if(EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            
            if(canvas.worldCamera == null)
                canvas.worldCamera = Camera.main;
            
            HandleButtonsState(scene);
        }

        private void HandleButtonsState(Scene scene)
        {
            foreach (NavButtonHandler navButtonHandler in navItems)
            {
                var isInteractable = navButtonHandler.IsInteractable;

                if (!isInteractable) continue;

                navButtonHandler.SetInactive();

                if (scene.name == scenes[navItems.IndexOf(navButtonHandler)].ToString())
                {
                    navButtonHandler.SetActive();
                }
                else
                    navButtonHandler.SetInactive();
            }
        }

        private void SubscribeButtonsOnClick()
        {
            foreach (NavButtonHandler navButtonHandler in navItems)
            {
                navButtonHandler.GetButton.onClick.AddListener(() =>
                {
                    if (SceneManager.GetActiveScene().name == scenes[navItems.IndexOf(navButtonHandler)].ToString()) return;
                    if (navButtonHandler.IsInteractable)
                    {
                        SceneManager.LoadSceneAsync(scenes[navItems.IndexOf(navButtonHandler)].ToString());
                    }
                });
            }
        }
    
    }
}
