using TMPro;
using UnityEngine;

namespace UI.Hud
{
    public class UpdateSpeed : MonoBehaviour
    {
        [SerializeField] TMP_Text text;
        [SerializeField] OutputVelocity outputVelocity;

        void Start()
        {
            if (outputVelocity is null) return; 
           
            if (text is null) GetComponentInChildren<TMP_Text>();
        }

        private void OnEnable()
        {
            outputVelocity.OnSpeedChanged += UpdateText;
        }
    
        private void OnDisable()
        {
            outputVelocity.OnSpeedChanged -= UpdateText;
        }
    
        void UpdateText(float speed)
        {
            text.text = speed.ToString("F0");
        }
    }
}
