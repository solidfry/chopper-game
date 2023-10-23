using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Enums;

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
        foreach (NavButtonHandler button in navItems)
        {
            if (scenes.FindIndex(n => n.ToString() == scene.name) == navItems.IndexOf(button))
            {
                button.SetActive();
                Debug.Log(scene.name);
            }
            else
                button.SetInactive();
        }
    }
    
}
