using System;
using UnityEngine;

/// <summary>네트워크 없이 몸통 8방향 이동, 손 애니메이션, 무기 장착을 제어함.</summary>
[RequireComponent(typeof(PropTestController))]
public class PropTestAnim : MonoBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>손 재장전/드로우 상태에 대응하는 총기 재장전 클립 프레임 구간.</summary>
    private readonly struct WeaponReloadSyncRule
    {
        public readonly int handStateHash;
        public readonly float totalHandFrames;
        public readonly float weaponStartFrame;
        public readonly float weaponTotalFrames;

        public WeaponReloadSyncRule(int handStateHash, float totalHandFrames, float weaponStartFrame, float weaponTotalFrames)
        {
            this.handStateHash = handStateHash;
            this.totalHandFrames = totalHandFrames;
            this.weaponStartFrame = weaponStartFrame;
            this.weaponTotalFrames = weaponTotalFrames;
        }
    }

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>무기 소켓과 맨손 손 클립을 읽어오는 프롭 데이터.</summary>
    [SerializeField] private PropDataSO propData;

    /// <summary>몸통 <see cref="Animator"/>. 컨트롤러는 프리팹에서 지정함.</summary>
    [SerializeField] private Animator bodyAnimator;

    /// <summary>무기 및 탄창을 장착하는 손.</summary>
    [SerializeField] private HandScript handScript;

    /// <summary>무기를 들었을 때 <c>MoveX</c>, <c>MoveY</c> 파라미터의 감쇠 시간 (초).</summary>
    [SerializeField] private float moveDampTime = 0.1f;

    private PropTestController controller;

    private Animator handAnimator;

    /// <summary><see cref="propData"/>로 생성한 손 오버라이드 컨트롤러.</summary>
    private RuntimeAnimatorController handOverrideController;

    /// <summary>오른손 무기의 <see cref="Animator"/>. 무기가 없거나 애니메이터가 없으면 null.</summary>
    private Animator weaponAnimator;

    /// <summary>손이 드로우 상태에 들어가면 <c>isWeaponSwap</c>을 내리기 위한 대기 플래그.</summary>
    private bool pendingWeaponSwapReset;

    /// <summary>손이 재장전 상태에 들어가면 <c>isReload</c>를 내리기 위한 대기 플래그.</summary>
    private bool pendingReloadReset;

    private const float HandShotFrames = 17f; // 손 발사 클립 총 프레임 수
    private const float WeaponShotFrames = 9f; // 총기 발사 클립 총 프레임 수

    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveY = Animator.StringToHash("MoveY");
    private static readonly int IsAttack = Animator.StringToHash("isAttack");
    private static readonly int IsAir = Animator.StringToHash("isAir");
    private static readonly int IsJumped = Animator.StringToHash("isJumped");
    private static readonly int WeaponMode = Animator.StringToHash("WeaponMode");
    private static readonly int IsWeaponSwap = Animator.StringToHash("isWeaponSwap");
    private static readonly int IsReload = Animator.StringToHash("isReload");

    /// <summary>몸통 맨손 이동 블렌드 상태.</summary>
    private static readonly int UnarmedBodyState = Animator.StringToHash("UnArmed");

    /// <summary>손 맨손 달리기 상태.</summary>
    private static readonly int UnarmedHandRunState = Animator.StringToHash("Run");

    private static readonly int[] HandShotStates =
    {
        Animator.StringToHash("M1911_Shot"),
        Animator.StringToHash("AK_Shot")
    };

    private static readonly int[] HandDrawStates =
    {
        Animator.StringToHash("Knife_Draw"),
        Animator.StringToHash("M1911_Draw"),
        Animator.StringToHash("AK_Draw")
    };

    private static readonly int[] HandReloadStates =
    {
        Animator.StringToHash("M1911_Reload"),
        Animator.StringToHash("AK_Reload")
    };

    private static readonly int WeaponIdleState = Animator.StringToHash("Idle");
    private static readonly int WeaponShotState = Animator.StringToHash("Shot");
    private static readonly int WeaponReloadState = Animator.StringToHash("Reload");

    /// <summary>손 재장전/드로우 상태별 총기 슬라이드/노리쇠 후퇴 프레임 동기화 테이블.</summary>
    private static readonly WeaponReloadSyncRule[] ReloadSyncRules =
    {
        new WeaponReloadSyncRule(HandReloadStates[0], 107f, 78f, 9f),
        new WeaponReloadSyncRule(HandReloadStates[1], 179f, 130f, 16f),
        new WeaponReloadSyncRule(HandDrawStates[1], 47f, 19f, 9f),
        new WeaponReloadSyncRule(HandDrawStates[2], 69f, 15f, 16f)
    };

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 장착 중인 무기 종류.</summary>
    public EWeaponType CurrentWeaponType { get; private set; } = EWeaponType.Hide;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        controller = GetComponent<PropTestController>();
        handAnimator = handScript.GetComponent<Animator>();
        handOverrideController = propData.CreateHandController();
        handAnimator.runtimeAnimatorController = handOverrideController;
    }

    private void Update()
    {
        Vector3 localVelocity = transform.InverseTransformDirection(controller.PlanarVelocity) / controller.MoveSpeed;
        float speedRatio = Mathf.Clamp01(localVelocity.magnitude);

        if (CurrentWeaponType == EWeaponType.Hide)
        {
            float moveY = speedRatio >= 0.1f ? 1f : 0f;
            bodyAnimator.SetFloat(MoveX, 0f);
            bodyAnimator.SetFloat(MoveY, moveY);
            handAnimator.SetFloat(MoveX, moveY);
        }
        else
        {
            bodyAnimator.SetFloat(MoveX, localVelocity.x, moveDampTime, Time.deltaTime);
            bodyAnimator.SetFloat(MoveY, localVelocity.z, moveDampTime, Time.deltaTime);
            handAnimator.SetFloat(MoveX, speedRatio, moveDampTime, Time.deltaTime);
        }

        int currentHash = handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int nextHash = handAnimator.IsInTransition(0) ? handAnimator.GetNextAnimatorStateInfo(0).shortNameHash : 0;

        // 드로우/재장전 상태에 들어가면 플래그를 내려 같은 전이가 반복되지 않게 함
        if (pendingWeaponSwapReset
            && (Array.IndexOf(HandDrawStates, currentHash) >= 0 || Array.IndexOf(HandDrawStates, nextHash) >= 0))
        {
            handAnimator.SetBool(IsWeaponSwap, false);
            pendingWeaponSwapReset = false;
        }

        if (pendingReloadReset
            && (Array.IndexOf(HandReloadStates, currentHash) >= 0 || Array.IndexOf(HandReloadStates, nextHash) >= 0))
        {
            handAnimator.SetBool(IsReload, false);
            SetWeaponAnimatorBool(IsReload, false);
            pendingReloadReset = false;
        }

        // 맨손 달리기 중 손 클립의 재생 위상을 몸통 클립에 맞춤
        if (CurrentWeaponType == EWeaponType.Hide
            && currentHash == UnarmedHandRunState
            && !handAnimator.IsInTransition(0)
            && !bodyAnimator.IsInTransition(0))
        {
            AnimatorStateInfo bodyInfo = bodyAnimator.GetCurrentAnimatorStateInfo(0);

            if (bodyInfo.shortNameHash == UnarmedBodyState)
            {
                float bodyPhase = Mathf.Repeat(bodyInfo.normalizedTime, 1f);
                float handPhase = Mathf.Repeat(handAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);

                if (Mathf.Abs(Mathf.DeltaAngle(bodyPhase * 360f, handPhase * 360f)) / 360f > 0.05f)
                {
                    handAnimator.Play(UnarmedHandRunState, 0, bodyPhase);
                }
            }
        }

        // 손의 발사, 재장전, 드로우 프레임에 맞춰 총기 애니메이터를 직접 재생함
        if (weaponAnimator == null || weaponAnimator.runtimeAnimatorController == null)
        {
            return;
        }

        int ruleIndex = Array.FindIndex(ReloadSyncRules, rule => rule.handStateHash == nextHash);
        bool useNextState = Array.IndexOf(HandShotStates, nextHash) >= 0 || ruleIndex >= 0;
        AnimatorStateInfo activeHandState = useNextState
            ? handAnimator.GetNextAnimatorStateInfo(0)
            : handAnimator.GetCurrentAnimatorStateInfo(0);
        int activeHash = activeHandState.shortNameHash;

        if (Array.IndexOf(HandShotStates, activeHash) >= 0)
        {
            float handFrame = Mathf.Repeat(activeHandState.normalizedTime, 1f) * HandShotFrames;

            if (handFrame < WeaponShotFrames)
            {
                weaponAnimator.SetBool(IsAttack, true);
                weaponAnimator.SetBool(IsReload, false);
                weaponAnimator.Play(WeaponShotState, 0, handFrame / WeaponShotFrames);
            }
            else
            {
                weaponAnimator.SetBool(IsAttack, false);
                weaponAnimator.Play(WeaponIdleState, 0, 0f);
            }

            return;
        }

        ruleIndex = Array.FindIndex(ReloadSyncRules, rule => rule.handStateHash == activeHash);

        if (ruleIndex >= 0)
        {
            WeaponReloadSyncRule rule = ReloadSyncRules[ruleIndex];
            float weaponFrame = Mathf.Clamp01(activeHandState.normalizedTime) * rule.totalHandFrames - rule.weaponStartFrame;

            if (weaponFrame >= 0f && weaponFrame < rule.weaponTotalFrames)
            {
                weaponAnimator.SetBool(IsReload, true);
                weaponAnimator.SetBool(IsAttack, false);
                weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / rule.weaponTotalFrames);
            }
            else
            {
                weaponAnimator.SetBool(IsReload, false);
                weaponAnimator.Play(WeaponIdleState, 0, 0f);
            }

            return;
        }

        weaponAnimator.SetBool(IsAttack, false);
        weaponAnimator.SetBool(IsReload, false);

        if (weaponAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash != WeaponIdleState)
        {
            weaponAnimator.Play(WeaponIdleState, 0, 0f);
        }
    }

    private void OnDestroy()
    {
        Destroy(handOverrideController);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>점프 및 체공 파라미터를 몸통과 손에 설정함.</summary>
    public void SetJumpState(bool isJumping, bool inAir)
    {
        bodyAnimator.SetBool(IsJumped, isJumping);
        bodyAnimator.SetBool(IsAir, inAir);
        handAnimator.SetBool(IsJumped, isJumping);
        handAnimator.SetBool(IsAir, inAir);
    }

    /// <summary>무기를 들고 있을 때만 공격 파라미터를 설정함.</summary>
    public void SetAttackState(bool attacking)
    {
        bool canAttack = attacking && CurrentWeaponType != EWeaponType.Hide;
        handAnimator.SetBool(IsAttack, canAttack);
        SetWeaponAnimatorBool(IsAttack, canAttack);
    }

    /// <summary>오른손 무기와 왼손 탄창을 <paramref name="weaponType"/>의 소켓대로 바꾸고 애니메이터에 알림.</summary>
    public void SetWeaponMode(EWeaponType weaponType)
    {
        if (weaponType == CurrentWeaponType)
        {
            return;
        }

        CurrentWeaponType = weaponType;
        int index = (int)weaponType;

        Destroy(handScript.GetLeftHandItem());
        Destroy(handScript.GetRightHandItem());
        weaponAnimator = null;

        WeaponSocketConfig[] sockets = propData.WeaponSockets;

        if (index < sockets.Length && sockets[index].weaponPrefab != null)
        {
            WeaponSocketConfig socket = sockets[index];
            GameObject weaponObject = Instantiate(socket.weaponPrefab);
            Collider weaponCollider = weaponObject.GetComponent<Collider>();

            if (weaponCollider != null)
            {
                weaponCollider.enabled = false;
            }

            handScript.SetRightHandItem(weaponObject);
            weaponObject.transform.SetLocalPositionAndRotation(socket.gripPosition, Quaternion.Euler(socket.gripRotation));
            weaponObject.transform.localScale = socket.gripScale;

            GunScript gun = weaponObject.GetComponent<GunScript>();

            if (gun != null && socket.reloadClip != null)
            {
                // 재장전 클립의 탄창 장착 시각으로 손을 옮겨, 왼손 탄창이 총의 탄창과 겹치는 자리에 생성함
                AnimationEvent equipEvent = Array.Find(socket.reloadClip.events,
                    evt => evt.functionName == nameof(HandScript.EquipMagazineToGun));

                if (equipEvent != null)
                {
                    socket.reloadClip.SampleAnimation(handScript.gameObject, equipEvent.time);
                }

                GameObject magazine = Instantiate(gun.GetMagazine(), handScript.GetLeftHand(), true);
                magazine.SetActive(false);
                handScript.SetLeftHandItem(magazine);
                handAnimator.Update(0f);
            }

            weaponAnimator = weaponObject.GetComponent<Animator>();

            if (weaponAnimator != null && socket.weaponAnimController != null)
            {
                weaponAnimator.runtimeAnimatorController = socket.weaponAnimController;
                weaponAnimator.Rebind();
                weaponAnimator.Play(WeaponIdleState, 0, 0f);
                weaponAnimator.Update(0f);
            }
        }

        bodyAnimator.SetInteger(WeaponMode, index);
        handAnimator.SetInteger(WeaponMode, index);
        handAnimator.SetBool(IsReload, false);
        handAnimator.SetBool(IsAttack, false);
        handAnimator.SetBool(IsWeaponSwap, weaponType != EWeaponType.Hide);
        pendingWeaponSwapReset = weaponType != EWeaponType.Hide;
        pendingReloadReset = false;
    }

    /// <summary>총을 들고 있고 드로우/재장전 중이 아닐 때 손 재장전 애니메이션을 시작함.</summary>
    public void RequestReload()
    {
        if (CurrentWeaponType != EWeaponType.Pistol && CurrentWeaponType != EWeaponType.Ak)
        {
            return;
        }

        if (pendingReloadReset || pendingWeaponSwapReset)
        {
            return;
        }

        int currentHash = handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int nextHash = handAnimator.IsInTransition(0) ? handAnimator.GetNextAnimatorStateInfo(0).shortNameHash : 0;

        if (Array.IndexOf(HandReloadStates, currentHash) >= 0 || Array.IndexOf(HandReloadStates, nextHash) >= 0
            || Array.IndexOf(HandDrawStates, currentHash) >= 0 || Array.IndexOf(HandDrawStates, nextHash) >= 0)
        {
            return;
        }

        handAnimator.SetBool(IsReload, true);
        SetWeaponAnimatorBool(IsReload, true);
        pendingReloadReset = true;
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>오른손 무기에 애니메이터와 컨트롤러가 있으면 <paramref name="id"/> 파라미터를 설정함.</summary>
    private void SetWeaponAnimatorBool(int id, bool value)
    {
        if (weaponAnimator != null && weaponAnimator.runtimeAnimatorController != null)
        {
            weaponAnimator.SetBool(id, value);
        }
    }
}
