using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkTestController : NetworkBehaviour
{
    private Keyboard _keyboard;

    private void Awake()
    {
        _keyboard = Keyboard.current;
    }

    void Update()
    {

        if (_keyboard.pKey.wasPressedThisFrame)
        {
            if (IsClient) return;

            StartHost();
            print("Host : " + IsHost);
        }

        if (_keyboard.oKey.wasPressedThisFrame)
        {
            if (IsHost) return;

            StartClient();
            print("Client : " + IsClient);
        }
    }

    void StartHost()
    {
        NetworkManager.Singleton.StartHost();

        ulong myLocalClientId = NetworkManager.Singleton.LocalClientId;
        print($"내 로컬 ClientId: {myLocalClientId}");
    }

    void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        ulong myLocalClientId = NetworkManager.Singleton.LocalClientId;
        print($"내 로컬 ClientId: {myLocalClientId}");
    }

}
