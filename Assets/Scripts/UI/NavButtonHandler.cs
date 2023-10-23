using UnityEngine;
using Button = UnityEngine.UI.Button;

public class NavButtonHandler : MonoBehaviour
{
    [SerializeField] bool isActiveView = false;
    [SerializeField] Color activeColor;
    [SerializeField] Color normalColor;
    private Button _button;

    private void Awake()
    {
        normalColor = GetComponent<Button>().colors.normalColor;
        _button = GetComponent<Button>();
    }

    private void Update()
    {
        if(isActiveView)
            _button.targetGraphic.canvasRenderer.SetColor(activeColor);
        else
            _button.targetGraphic.canvasRenderer.SetColor(normalColor);

    }
    
    public void SetActive()
    {
        isActiveView = true;
    }
    
    public void SetInactive()
    {
        isActiveView = false;
    }
}
