using System;
using TMPro;
using UnityEngine;

namespace UI.Hud
{
    [Serializable]
    public class SpeedUI
    {
        [SerializeField] TMP_Text text;
        
        public void UpdateText(float speed)
        {
            text.text = speed.ToString("F0");
        }
    }
}
