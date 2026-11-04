using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TestController : NetworkBehaviour
{

    private Keyboard _keyboard;

    [SerializeField] private Animator _animator;
    [SerializeField] private Rigidbody _rb;

    [SerializeField] private Transform _transform;
    private Vector3 _moveDir;

    private void Awake()
    {
        _keyboard = Keyboard.current;

        _animator = GetComponent<Animator>();
        _rb = GetComponent<Rigidbody>();
        _transform = GetComponent<Transform>();


    }

    void Start()
    {

    }

 
    void Update()
    {
        if(_keyboard.wKey.isPressed )
        {
            // 자기 소유의 오브젝트가 아니면 RPC 호출 아예 원천 차단
            if (!IsOwner) return;

            MoveServerRpc();
        }
    }



    [ServerRpc]
    void MoveServerRpc()
    {
            transform.Translate(Vector3.forward * 1f * Time.deltaTime);
            _animator.SetFloat("MoveX",1f);
        
    }

    [Rpc(SendTo.ClientsAndHost)]
    void MoveServerRpc(Vector3 moveDir)
    {
        
    }

}
