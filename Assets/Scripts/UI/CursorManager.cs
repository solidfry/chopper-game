using System.Collections.Generic;
using Enums;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace UI
{
    public class CursorManager : SingletonPersistent<CursorManager>
    {
        [SerializeField] Texture2D defaultTexture;
        [SerializeField] Texture2D hoverTexture;
        [SerializeField] Texture2D clickTexture;
        [SerializeField] LayerMask buttonLayerMask;
        
        PointerEventData _pointerEventData;
        
        [SerializeField] List<Scenes> scenesToDisableCursor;
    
        private Mouse _mouse; 

        void Start()
        {
            // If the player is using the mouse and keyboard, we want to show the cursor
            if (Mouse.current == null) return;
            
            _mouse = Mouse.current;
            Cursor.SetCursor( defaultTexture, Vector2.zero, CursorMode.Auto);
            _pointerEventData = new PointerEventData(EventSystem.current) { position = _mouse.position.ReadValue() };
        }
        
        void Update() => HandleMouseState();

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            if(EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            ToggleCursorInSelectScenes();
        }
        
        private void ToggleCursorInSelectScenes() => Cursor.visible =
            !scenesToDisableCursor.Exists(scene => scene.ToString() == SceneManager.GetActiveScene().name);

        private void HandleMouseState()
        {
            if (EventSystem.current == null) return;
            
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(_pointerEventData, results);

            bool isOverButton = results.Exists(result => ((1 << result.gameObject.layer) & buttonLayerMask) != 0);

            Texture2D cursorTexture = _mouse.leftButton.isPressed ? clickTexture : (isOverButton ? hoverTexture : defaultTexture);
        
            SetCursor(cursorTexture);
        }
        
        void SetCursor(Texture2D texture) => Cursor.SetCursor(texture, Vector2.zero, CursorMode.Auto);

        void DisableCursor() => Cursor.visible = false;

        void EnableCursor() => Cursor.visible = true;
    }
}
