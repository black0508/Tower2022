using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tower
{
    public class VoteOptionButton : MonoBehaviour
    {
        [SerializeField] Text nameText;
        [SerializeField] Text descText;
        [SerializeField] Text buttonText;
        [SerializeField] Button button;

        public void Bind(string name, string desc, Action onClick)
        {
            if (nameText != null) nameText.text = name;
            if (descText != null) descText.text = desc;
            SetCount(0, 1);
            if (button == null) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        public void SetCount(int count, int total)
        {
            if (buttonText != null)
                buttonText.text = $"确定({count}/{Mathf.Max(1, total)})";
        }
    }
}
