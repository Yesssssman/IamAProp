using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using Networking;
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
        private const float STOP_TIMEOUT_SECONDS = 1.5f;

        [SerializeField] private Button gameQuitButton;

        /// <summary>이벤트를 구독한 FishNet <see cref="NetworkManager"/>.</summary>
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
            LobbyManager.RoomClosed += HandleRoomClosed;
            LobbyManager.Disconnected += HandleRoomDisconnected;

            network = InstanceFinder.NetworkManager;

            if (network != null)
            {
                network.ClientManager.OnClientConnectionState += HandleClientConnectionState;
            }
        }

        private void OnDestroy()
        {
            LobbyManager.GameEnded -= HandleGameEnded;
            LobbyManager.RoomClosed -= HandleRoomClosed;
            LobbyManager.Disconnected -= HandleRoomDisconnected;

            if (network != null)
            {
                network.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
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

            await LobbyManager.LeaveRoom();
            await ReturnToLobbyScene();
        }

        private async void HandleGameEnded()
        {
            await ReturnToLobbyScene();
        }

        private async void HandleRoomClosed(string reason)
        {
            await ReturnToLobbyScene();
        }

        private async void HandleRoomDisconnected()
        {
            await ReturnToLobbyScene();
        }

        /// <summary>클라이언트가 호스트와의 연결을 잃으면 로비 씬으로 돌아감.</summary>
        private async void HandleClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
            {
                return;
            }

            if (network != null && network.ServerManager.Started)
            {
                return;
            }

            await ReturnToLobbyScene();
        }

        /// <summary>FishNet 연결을 종료하고 <see cref="NetworkManager"/>를 정리한 뒤 로비 씬을 불러옴.</summary>
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
                network.ClientManager.OnClientConnectionState -= HandleClientConnectionState;

                if (network.ClientManager.Started)
                {
                    network.ClientManager.StopConnection();
                }

                if (network.ServerManager.Started)
                {
                    network.ServerManager.StopConnection(true);
                }

                float elapsed = 0f;
                while (network != null
                    && (network.ClientManager.Started || network.ServerManager.Started)
                    && elapsed < STOP_TIMEOUT_SECONDS)
                {
                    elapsed += Time.unscaledDeltaTime;
                    await Awaitable.NextFrameAsync();
                    if (this == null) return;
                }

                if (network != null)
                {
                    Destroy(network.gameObject);
                    await Awaitable.NextFrameAsync();
                }
            }

            SceneManager.LoadScene(LOBBY_SCENE);
        }
    }
}
