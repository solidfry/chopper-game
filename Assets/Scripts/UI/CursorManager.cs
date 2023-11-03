using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;

public class CursorManager : SingletonPersistent<CursorManager>
{
    [SerializeField] Texture2D defaultTexture;
    [SerializeField] Texture2D hoverTexture;
    [SerializeField] Texture2D clickTexture;
    [SerializeField] LayerMask buttonLayerMask;
    
    private Mouse mouse; 

    // Start is called before the first frame update
    void Start()
    {
        mouse = Mouse.current;
        Cursor.SetCursor( null, Vector2.zero, CursorMode.Auto);
    }
    
    
    void Update()
    { 
        HandleMouseState();
    }

    private void HandleMouseState()
    {
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = mouse.position.ReadValue() }, results);

        bool isOverButton = results.Exists(result => ((1 << result.gameObject.layer) & buttonLayerMask) != 0);

        Texture2D cursorTexture = mouse.leftButton.isPressed ? clickTexture : (isOverButton ? hoverTexture : defaultTexture);
        
        SetCursor(cursorTexture);
    }


    void SetCursor(Texture2D texture)
    {
        Cursor.SetCursor(texture, Vector2.zero, CursorMode.Auto);
    }
}
