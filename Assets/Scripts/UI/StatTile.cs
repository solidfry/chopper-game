using TMPro;
using UI.ScriptableObjects;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    public class StatTile : MonoBehaviour
    {
        [SerializeField] ColourData colourData;
    
        [Header("UI Elements")]
        [SerializeField] Image[] borders;
        [SerializeField] Image backgroundImage;

        [Header("Label Text")]
        [SerializeField] private string label;
        [SerializeField] TMP_Text labelField;
        
        [Header("Value Text")]
        [SerializeField] private string value;
        [SerializeField] TMP_Text valueField;
        
        private void OnValidate() => UpdateValues();

        private void Start() => UpdateValues();

        private void UpdateValues()
        {
            if (colourData == null || borders == null || backgroundImage == null) return;
            SetColour(colourData);
            SetLabel();
            SetValue();
        }
        
        void SetLabel()
        {
            labelField.text = label;
        }
        
        public void SetStat(string newValue)
        {
            value = newValue;
            SetValue();
        }

        private void SetValue()
        {
            valueField.text = value;
        }

        void SetColour(ColourData cData)
        {
            colourData = cData;
            foreach (var border in borders)
            {
                border.color = colourData.Color;
            }
        
            backgroundImage.color = colourData.ShadeColor;
        
            valueField.color = colourData.Color;
            labelField.color = colourData.Color;
        }
    
    
    }
}


