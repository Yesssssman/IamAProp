using System;
using UnityEngine;

/// <summary>손에 든 수류탄. 핀을 뽑아 떨어트리고, Rigidbody로 시뮬레이션되는 수류탄 복제본을 던짐.</summary>
public class GrenadeScript : MonoBehaviour
{
    /// <summary>수류탄의 자식인 핀.</summary>
    [SerializeField]
    private GameObject pin;

    /// <summary>뽑은 핀을 떨어트리는 속도 (m/s).</summary>
    [SerializeField]
    private float pinLaunchSpeed = 0.5f;

    /// <summary>핀을 뽑은 직후 호출. 인자는 떨어진 핀 복제본.</summary>
    public event Action<GameObject> PinRemoved;

    /// <summary>수류탄을 던진 직후 호출. 인자는 던져진 수류탄 복제본.</summary>
    public event Action<GameObject> Thrown;

    /// <summary>핀을 숨기고, 그 자리에서 Rigidbody로 시뮬레이션되는 핀 복제본을 떨어트림.</summary>
    /// <param name="launchDirection">핀을 떨어트리는 방향 (월드 공간).</param>
    public void RemovePin(Vector3 launchDirection)
    {
        if (!pin.activeSelf)
        {
            return;
        }

        GameObject droppedPin = Launch(pin, launchDirection.normalized * pinLaunchSpeed);
        pin.SetActive(false);
        PinRemoved?.Invoke(droppedPin);
    }

    /// <summary>손의 수류탄을 숨기고, 그 자리에서 Rigidbody로 시뮬레이션되는 수류탄 복제본을 던짐.</summary>
    /// <param name="velocity">던지는 속도 (월드 공간, m/s).</param>
    public void Throw(Vector3 velocity)
    {
        GameObject thrownGrenade = Launch(gameObject, velocity);
        gameObject.SetActive(false);
        Thrown?.Invoke(thrownGrenade);
    }

    /// <summary>던진 수류탄을 다시 보여주고, <paramref name="pinned"/>에 따라 핀을 보여주거나 숨김.</summary>
    public void ResetGrenade(bool pinned)
    {
        gameObject.SetActive(true);
        pin.SetActive(pinned);
    }

    /// <summary><paramref name="source"/>를 같은 자리에 복제해 월드에 두고 <paramref name="velocity"/>로 날려 보냄.</summary>
    private static GameObject Launch(GameObject source, Vector3 velocity)
    {
        GameObject launched = Instantiate(source, source.transform.parent);
        launched.transform.SetParent(null, true);

        Rigidbody launchedRigidbody = launched.GetComponent<Rigidbody>();
        launchedRigidbody.isKinematic = false;
        launchedRigidbody.linearVelocity = velocity;
        launched.GetComponent<Collider>().enabled = true;
        launched.GetComponent<DroppedMagazine>().enabled = true;

        return launched;
    }
}
