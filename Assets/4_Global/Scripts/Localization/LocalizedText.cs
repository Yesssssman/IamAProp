using UnityEngine;
using UnityEngine.UI;

namespace Localization
{
    /// <summary>같은 GameObject의 <see cref="Text"/>를 <see cref="key"/>의 현재 언어 문장으로 채움.</summary>
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        /// <summary><see cref="LanguageTable"/>의 언어 키.</summary>
        [SerializeField] private string key;

        private Text text;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            text = GetComponent<Text>();
        }

        private void OnEnable()
        {
            Language.Changed += HandleLanguageChanged;
            HandleLanguageChanged();
        }

        private void OnDisable()
        {
            Language.Changed -= HandleLanguageChanged;
        }

        private void HandleLanguageChanged()
        {
            text.text = Language.Get(key);
        }
    }
}
