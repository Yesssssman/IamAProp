using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public sealed class BananaFirePreview : MonoBehaviour
{
    [SerializeField] private Animator handAnimator;
    [SerializeField] private ParticleSystem casingParticles;
    [SerializeField] private AudioSource shotAudio;
    [SerializeField, Min(0.01f)] private float fireInterval = 0.1f;

    private static readonly int ShotState = Animator.StringToHash("Base Layer.Shot");

    private Animator _weaponAnimator;
    private float _nextFireTime;

    private void Awake()
    {
        _weaponAnimator = GetComponent<Animator>();
    }

    private void Update()
    {
        var space = Keyboard.current?.spaceKey;
        var click = Mouse.current?.leftButton;
        bool fireHeld = (space != null && (space.isPressed || space.wasPressedThisFrame))
            || (click != null && (click.isPressed || click.wasPressedThisFrame));
        if (!fireHeld || Time.time < _nextFireTime) return;

        _nextFireTime = Time.time + fireInterval;
        handAnimator.Play(ShotState, 0, 0f);
        _weaponAnimator.Play(ShotState, 0, 0f);
    }

    /// <summary>발사 클립의 프레임에 맞춰 탄피와 소리를 재생한다.</summary>
    public void EmitCasing()
    {
        casingParticles.Emit(1);
        if (shotAudio.clip != null) shotAudio.PlayOneShot(shotAudio.clip);
    }
}
