using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;
public class NavigationManager : MonoBehaviour
{

    [SerializeField] List<Button> navItems;
    [SerializeField] List<string> sceneNames;

    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        foreach (Button button in navItems)
        {
            if (sceneNames.FindIndex((name) => name == scene.name) == navItems.IndexOf(button))
                SetButtonState(button, false);
            else
                SetButtonState(button, true);
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }



    void SetButtonState(Button button, bool state)
    {
        button.interactable = state;
        button.IsActive();
    }
}
