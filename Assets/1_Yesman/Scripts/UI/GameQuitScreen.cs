using Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UI
{
    /// <summary>
    /// 인게임에서 게임을 나가는 테스트 UI. 방장은 게임을 끝내고, 나머지는 방을 나감.
    /// 방이 닫히거나 대기실·호스트 연결이 끊겨도 로비 씬으로 돌아감.
    /// </summary>
    public class GameQuitScreen : MonoBehaviour
    {
        private const string LOBBY_SCENE = "LobbyScene";

        [SerializeField] private Button gameQuitButton;

        /// <summary>이벤트를 구독한 NetworkManager. 이 씬에 들어올 때의 <see cref="NetworkManager.Singleton"/>.</summary>
        private NetworkManager network;

        /// <summary>로비 씬으로 돌아가는 중이면 true.</summary>
        private bool isReturning;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            gameQuitButton.onClick.AddListener(HandleGameQuitClicked);
            LobbyManager.GameEnded += HandleGameEnded;
            LobbyManager.LobbyClosed += HandleLobbyClosed;
            LobbyManager.Disconnected += HandleLobbyDisconnected;

            network = NetworkManager.Singleton;

            if (network != null)
            {
                network.OnConnectionEvent += HandleConnectionEvent;
            }
        }

        private void OnDestroy()
        {
            LobbyManager.GameEnded -= HandleGameEnded;
            LobbyManager.LobbyClosed -= HandleLobbyClosed;
            LobbyManager.Disconnected -= HandleLobbyDisconnected;

            if (network != null)
            {
                network.OnConnectionEvent -= HandleConnectionEvent;
            }
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private async void HandleGameQuitClicked()
        {
            gameQuitButton.interactable = false;

            if (LobbyManager.IsHost)
            {
                // 서버가 승인하면 방장 포함 모든 멤버가 GameEnded를 받아 대기실로 돌아감
                if (!await LobbyManager.EndGame())
                {
                    gameQuitButton.interactable = true;
                }

                return;
            }

            await LobbyManager.LeaveLobby();
            await ReturnToLobbyScene();
        }

        private async void HandleGameEnded()
        {
            await ReturnToLobbyScene();
        }

        private async void HandleLobbyClosed(string reason)
        {
            await ReturnToLobbyScene();
        }

        private async void HandleLobbyDisconnected()
        {
            await ReturnToLobbyScene();
        }

        /// <summary>클라이언트가 호스트와의 연결을 잃으면 로비 씬으로 돌아감.</summary>
        private async void HandleConnectionEvent(NetworkManager manager, ConnectionEventData data)
        {
            // 호스트에서는 다른 클라이언트가 나갈 때도 ClientDisconnected가 오므로 무시
            if (manager.IsServer || data.EventType != ConnectionEvent.ClientDisconnected || data.ClientId != manager.LocalClientId)
            {
                return;
            }

            await ReturnToLobbyScene();
        }

        /// <summary>
        /// Netcode를 끄고 로비 씬을 불러옴.
        ///
        /// 로비 씬에도 NetworkManager가 있어서 DontDestroyOnLoad로 남은 기존 것을 먼저 지움. NetworkManager는
        /// OnEnable에서 Singleton이 비어 있을 때만 자신을 Singleton으로 등록하므로, 기존 것이 실제로 파괴되기
        /// 전에 새 것이 켜지면 Singleton이 null로 남음. 그래서 한 프레임 기다린 뒤 씬을 불러옴.
        /// </summary>
        private async Awaitable ReturnToLobbyScene()
        {
            // 방장의 게임 종료 시 클라이언트는 호스트 연결 끊김과 GameEnded를 둘 다 받을 수 있음
            if (isReturning)
            {
                return;
            }

            isReturning = true;

            if (network != null)
            {
                network.OnConnectionEvent -= HandleConnectionEvent;

                if (network.IsListening)
                {
                    network.Shutdown();
                }

                Destroy(network.gameObject);
                await Awaitable.NextFrameAsync();
            }

            SceneManager.LoadScene(LOBBY_SCENE);
        }
    }
}
