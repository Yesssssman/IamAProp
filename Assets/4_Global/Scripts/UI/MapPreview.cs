using Localization;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 맵 미리보기와 맵 이름. 미리보기 위에 비네트가 덮여 있음.
    /// PLACEHOLDER: 맵별 미리보기 이미지가 아직 없어 빈 패널만 보여줌.
    /// </summary>
    public class MapPreview : MonoBehaviour
    {
        [SerializeField] private Text mapNameText;

        /// <summary>마지막으로 보여준 맵.</summary>
        private EGameMap map;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

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
            mapNameText.text = Language.Get(LanguageTable.MAP_PREFIX + map);
        }

        // ***************************************************************
        // 공개
        // ***************************************************************

        public void Show(EGameMap shownMap)
        {
            map = shownMap;
            HandleLanguageChanged();
        }
    }
}
