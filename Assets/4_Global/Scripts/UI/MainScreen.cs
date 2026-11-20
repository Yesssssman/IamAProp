using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>타이틀과 게임 만들기·참가·설정·종료 버튼.</summary>
    public class MainScreen : MonoBehaviour
    {
        [SerializeField] private Button createGameButton;
        [SerializeField] private Button joinGameButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        private LobbySceneUI ui;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            ui = GetComponentInParent<LobbySceneUI>(true);
            createGameButton.onClick.AddListener(ui.ShowCreateGame);
            joinGameButton.onClick.AddListener(HandleJoinGameClicked);
            settingsButton.onClick.AddListener(ui.ShowSettings);
            quitButton.onClick.AddListener(HandleQuitClicked);
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandleJoinGameClicked()
        {
            ui.ShowLobby();
        }

        private void HandleQuitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
