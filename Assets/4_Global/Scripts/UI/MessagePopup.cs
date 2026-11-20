using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>화면 전체를 덮는 반투명 검은 배경 위에 메세지와 확인 버튼을 띄우는 팝업.</summary>
    public class MessagePopup : MonoBehaviour
    {
        [SerializeField] private Text messageText;
        [SerializeField] private Button okButton;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            okButton.onClick.AddListener(HandleOkClicked);
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandleOkClicked()
        {
            gameObject.SetActive(false);
        }

        // ***************************************************************
        // 공개
        // ***************************************************************

        /// <summary><paramref name="message"/>를 띄우고 다른 화면보다 위에 놓음.</summary>
        public void Show(string message)
        {
            messageText.text = message;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }
    }
}
