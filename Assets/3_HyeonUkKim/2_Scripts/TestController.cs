using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TestController : NetworkBehaviour
{

    private Keyboard _keyboard;

    private Vector3 _moveDir;

    private void Awake()
    {
        _keyboard = Keyboard.current;
    }

    void Start()
    {

    }

 
    void Update()
    {
        if (_keyboard.wKey.isPressed)
        {
            Move();
        }



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


    void Move()
    {
        if (_keyboard.wKey.isPressed)
        {
            _moveDir += Vector3.forward;
        }
    }

    [ServerRpc]
    void MoveServerRpc(Vector3 moveDir)
    {
        
    }

}
