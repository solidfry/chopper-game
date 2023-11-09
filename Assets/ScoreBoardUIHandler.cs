using DG.Tweening;
using Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class ScoreBoardUIHandler : MonoBehaviour
{
    [SerializeField] RectTransform scoreboardObject;
    [SerializeField] InputAction showScoreboardAction;
    [SerializeField] UnityEvent onScoreboardShown;
    [SerializeField] UnityEvent onScoreboardHidden;


    private void Start() => scoreboardObject.gameObject.SetActive(true);

    private void OnEnable()
    {
        if (scoreboardObject == null) return;
        showScoreboardAction.Enable();

        showScoreboardAction.performed += ToggleScoreboard;
        showScoreboardAction.canceled += ToggleScoreboard;
    }

    private void OnDisable()
    {
        if (scoreboardObject == null) return;
        showScoreboardAction.Disable();

        showScoreboardAction.performed -= ToggleScoreboard;
        showScoreboardAction.canceled -= ToggleScoreboard;
    }

    private void ToggleScoreboard(InputAction.CallbackContext ctx)
    {
        if (ctx.performed)
        {
            // Debug.Log("Pressed");
            onScoreboardShown?.Invoke();
        }
        else if (ctx.canceled)
        {
            // Debug.Log("UnPressed");
            onScoreboardHidden?.Invoke();
        }
    }
}
