using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace UI
{
    public class CursorManager : SingletonPersistent<CursorManager>
    {
        [SerializeField] Texture2D defaultTexture;
        [SerializeField] Texture2D hoverTexture;
        [SerializeField] Texture2D clickTexture;
        [SerializeField] LayerMask buttonLayerMask;
        PointerEventData _pointerEventData;
    
        private Mouse _mouse; 

        // Start is called before the first frame update
        void Start()
        {
            _mouse = Mouse.current;
            Cursor.SetCursor( defaultTexture, Vector2.zero, CursorMode.Auto);
            _pointerEventData = new PointerEventData(EventSystem.current) { position = _mouse.position.ReadValue() };
        }
        
        void Update()
        { 
            HandleMouseState();
        }

        private void HandleMouseState()
        {
            if (EventSystem.current == null) return;
            
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(_pointerEventData, results);

            bool isOverButton = results.Exists(result => ((1 << result.gameObject.layer) & buttonLayerMask) != 0);

            Texture2D cursorTexture = _mouse.leftButton.isPressed ? clickTexture : (isOverButton ? hoverTexture : defaultTexture);
        
            SetCursor(cursorTexture);
        }
        
        void SetCursor(Texture2D texture)
        {
            Cursor.SetCursor(texture, Vector2.zero, CursorMode.Auto);
        }
        
        void DisableCursor()
        {
            Cursor.visible = false;
        }
    }
}
