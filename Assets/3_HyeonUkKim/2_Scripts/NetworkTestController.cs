using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

public class NetworkTestController : NetworkBehaviour
{
    private Keyboard _keyboard;

    [SerializeField] private Transform _spawnPos;
    [SerializeField] private GameObject _player;
    [SerializeField] private CinemachineCamera _cineCam;


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
            print($"Client : {IsClient}");
        }

        if (_keyboard.tKey.wasPressedThisFrame)
        {
            var connectedClientId = NetworkManager.Singleton.ConnectedClients;

            foreach (var clientId in connectedClientId)
            {
                print($" ClientKey : {clientId.Key} ClientIdValue : {clientId.Value.ClientId}, {clientId.Value}");
            }

        }
    }

    void StartHost()
    {
        NetworkManager.Singleton.StartHost();

        ulong myLocalClientId = NetworkManager.Singleton.LocalClientId;
        print($"내 로컬 ClientId: {myLocalClientId}");
        //PlayerSpawn(myLocalClientId);
    }

    void StartClient()
    {
        NetworkManager.Singleton.StartClient();


        
        ulong myLocalClientId = NetworkManager.Singleton.LocalClientId;
        print($"내 로컬 ClientId: {myLocalClientId}");
    }

    //void PlayerSpawn(ulong clientId)
    //{
    //    if (IsClient) return;

    //    GameObject playerInstance = Instantiate(_player, _spawnPos.position, _spawnPos.rotation);

    //    // 핵심: SpawnAsPlayerObject로 해당 클라이언트에게 소유권을 주며 스폰
    //    playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

    //    _cineCam.Target.TrackingTarget = playerInstance.transform;
    //    _cineCam.Target.LookAtTarget = playerInstance.transform;


    //    print($"Client {clientId} 스폰 완료");
    //}

}
