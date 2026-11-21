using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>아이디·닉네임·비밀번호로 가입하는 화면. 가입에 성공하면 로그인된 상태로 메인 화면으로 감.</summary>
    public class SignUpScreen : MonoBehaviour
    {
        private const int MIN_PASSWORD_LENGTH = 6; // 서버 비밀번호 최소 길이

        [SerializeField] private InputField idInput;

        /// <summary>다른 플레이어에게 보이는 이름.</summary>
        [SerializeField] private InputField nicknameInput;

        [SerializeField] private InputField passwordInput;
        [SerializeField] private InputField passwordCheckInput;
        [SerializeField] private Button okButton;
        [SerializeField] private Button cancelButton;

        /// <summary>입력 오류와 가입 실패를 보여주는 텍스트.</summary>
        [SerializeField] private Text statusText;

        private LobbySceneUI ui;

        /// <summary>가입 응답을 기다리는 중이면 true.</summary>
        private bool isSigningUp;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            ui = GetComponentInParent<LobbySceneUI>(true);
            okButton.onClick.AddListener(HandleOkClicked);
            cancelButton.onClick.AddListener(ui.ShowLogin);
            passwordCheckInput.onSubmit.AddListener(HandlePasswordCheckSubmitted);
        }

        private void OnEnable()
        {
            statusText.text = "";
            idInput.text = "";
            nicknameInput.text = "";
            passwordInput.text = "";
            passwordCheckInput.text = "";
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandlePasswordCheckSubmitted(string password)
        {
            HandleOkClicked();
        }

        private async void HandleOkClicked()
        {
            if (isSigningUp)
            {
                return;
            }

            string id = idInput.text.Trim();

            if (id.Length == 0)
            {
                statusText.text = Language.Get(LanguageTable.LOGIN_ENTER_ID);
                return;
            }

            string nickname = nicknameInput.text.Trim();

            if (nickname.Length == 0)
            {
                statusText.text = Language.Get(LanguageTable.SIGN_UP_ENTER_NICKNAME);
                return;
            }

            if (passwordInput.text.Length < MIN_PASSWORD_LENGTH)
            {
                statusText.text = Language.Get(LanguageTable.SIGN_UP_PASSWORD_TOO_SHORT);
                return;
            }

            if (passwordInput.text != passwordCheckInput.text)
            {
                statusText.text = Language.Get(LanguageTable.SIGN_UP_PASSWORD_MISMATCH);
                return;
            }

            // 실명 입력이 없어 닉네임을 실명으로도 씀
            GameUserRequestDTO request = new GameUserRequestDTO
            {
                memberType = 0,
                memberId = id,
                memberPw = passwordInput.text,
                memberName = nickname,
                nickName = nickname,
            };

            isSigningUp = true;
            okButton.interactable = false;
            statusText.text = "";

            GameUserResponseDTO response = await LoginManager.SignUp(request);
            isSigningUp = false;

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
