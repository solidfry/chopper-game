using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace UI.Hud
{
    [Serializable]
    public abstract class TextUI<T>
    {
        public T value;
        [field: SerializeField] public TMPro.TMP_Text Text;
        
        public void UpdateText(T value)
        {
            this.value = value;
            Text.text = value.ToString();
        }
    }
}