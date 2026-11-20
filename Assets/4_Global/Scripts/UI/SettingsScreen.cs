using Localization;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 설정 화면. 언어만 적용됨.
    /// PLACEHOLDER: 마우스 감도·볼륨 입력은 모양만 있고 아직 어디에도 적용되지 않음.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        /// <summary>항목 순서는 <see cref="ELanguage"/> 순서. 항목은 프리팹에 들어 있음.</summary>
        [SerializeField] private Dropdown languageDropdown;

        [SerializeField] private Button backButton;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            LobbySceneUI ui = GetComponentInParent<LobbySceneUI>(true);
            languageDropdown.onValueChanged.AddListener(HandleLanguageSelected);
            backButton.onClick.AddListener(ui.ShowMain);
        }

        private void OnEnable()
        {
            languageDropdown.SetValueWithoutNotify((int)Language.Current);
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandleLanguageSelected(int index)
        {
            Language.Current = (ELanguage)index;
        }
    }
}
