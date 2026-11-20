using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>아이디·비밀번호로 로그인하는 화면. 회원가입 화면으로 가는 버튼이 있음.</summary>
    public class LoginScreen : MonoBehaviour
    {
        [SerializeField] private InputField idInput;
        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button okButton;

        /// <summary>"Sign up now!" 글자 버튼.</summary>
        [SerializeField] private Button signUpButton;

        /// <summary>입력 오류와 로그인 실패를 보여주는 텍스트.</summary>
        [SerializeField] private Text statusText;

        private LobbySceneUI ui;

        /// <summary>로그인 응답을 기다리는 중이면 true.</summary>
        private bool isLoggingIn;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            ui = GetComponentInParent<LobbySceneUI>(true);
            okButton.onClick.AddListener(HandleOkClicked);
            signUpButton.onClick.AddListener(ui.ShowSignUp);
            passwordInput.onSubmit.AddListener(HandlePasswordSubmitted);
        }

        private void OnEnable()
        {
            statusText.text = "";
            passwordInput.text = "";
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandlePasswordSubmitted(string password)
        {
            HandleOkClicked();
        }

        private async void HandleOkClicked()
        {
            if (isLoggingIn)
            {
                return;
            }

            string id = idInput.text.Trim();

            if (id.Length == 0)
            {
                statusText.text = Language.Get(LanguageTable.LOGIN_ENTER_ID);
                return;
            }

            if (passwordInput.text.Length == 0)
            {
                statusText.text = Language.Get(LanguageTable.LOGIN_ENTER_PASSWORD);
                return;
            }

            isLoggingIn = true;
            okButton.interactable = false;
            statusText.text = "";

            GameUserResponseDTO response = await LoginManager.LogIn(new GameUserRequestDTO { memberId = id, memberPw = passwordInput.text });
            isLoggingIn = false;

            if (this == null)
            {
                return;
            }

            okButton.interactable = true;

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                ui.ShowMain();
            }
            else
            {
                statusText.text = Language.GetResponseMessage(response.responseMessage);
            }
        }
    }
}
