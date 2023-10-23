using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Enums;
using UI;

public class NavigationManager : SingletonPersistent<NavigationManager>
{
    Canvas canvas;
    
    [SerializeField] List<NavButtonHandler> navItems;
    [SerializeField] List<Scenes> scenes;

    public override void Awake()
    {
        base.Awake();
        canvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        canvas.worldCamera = Camera.main;
        foreach (NavButtonHandler navButtonHandler in navItems)
        {
            var isInteractable = navButtonHandler.IsInteractable;
            
            if(isInteractable)
            {
                if (scenes.FindIndex(n => n.ToString() == scene.name) == navItems.IndexOf(navButtonHandler))
                {
                    navButtonHandler.SetActive();
                    Debug.Log(scene.name);
                }
                else
                    navButtonHandler.SetInactive();
            }
        }
    }
    
}
