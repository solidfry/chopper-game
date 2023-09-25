using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UpdateSpeed : MonoBehaviour
{
    TMP_Text text;
    // Start is called before the first frame update
    void Start()
    {
        text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        OutputVelocity.onSpeedChanged += UpdateText;
    }
    
    private void OnDisable()
    {
        OutputVelocity.onSpeedChanged -= UpdateText;
    }
    
    void UpdateText(float speed)
    {
        text.text = speed.ToString("F0");
    }
}
