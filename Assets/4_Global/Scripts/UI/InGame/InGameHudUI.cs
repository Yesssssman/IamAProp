using UnityEngine;
using UnityEngine.UI;

/// <summary>로컬 소유자 플레이어의 체력, 장착 무기, 탄창 및 5칸 장비 툴바를 표시하는 인게임 HUD 컨트롤러.</summary>
public class InGameHudUI : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float HealthSnapEpsilon = 0.0005f;

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>표시/숨김을 일괄 전환할 HUD 콘텐츠 루트 오브젝트.</summary>
    [Header("루트 및 표시 설정")]
    [SerializeField] private GameObject contentRoot;

    /// <summary>로컬 플레이어 사망 및 관전 진입 시 HUD를 숨길지 여부.</summary>
    [SerializeField] private bool hideOnDeath = true;

    /// <summary>로컬 플레이어가 아직 바인딩되지 않았을 때 HUD를 숨길지 여부.</summary>
    [SerializeField] private bool hideWhenUnbound = true;

    /// <summary>Filled 방식으로 체력 비율을 표시하는 체력 바 이미지.</summary>
    [Header("체력 HUD (좌상단)")]
    [SerializeField] private Image healthFillImage;

    /// <summary>슬라이더 방식으로 체력 비율을 표시할 때 연결하는 선택 참조.</summary>
    [SerializeField] private Slider healthSlider;

    /// <summary>현재 체력 / 최대 체력 수치 텍스트.</summary>
    [SerializeField] private Text healthValueText;

    /// <summary>정상 체력 구간의 체력 바 색상.</summary>
    [SerializeField] private Color fullHealthColor = new Color(0.24f, 0.82f, 0.45f, 1f);

    /// <summary>저체력 경고 구간의 체력 바 색상.</summary>
    [SerializeField] private Color lowHealthColor = new Color(0.92f, 0.25f, 0.25f, 1f);

    /// <summary>저체력 색상으로 전환되는 체력 비율 기준값 (0~1).</summary>
    [Range(0f, 1f)]
    [SerializeField] private float lowHealthThreshold = 0.3f;

    /// <summary>체력 바 채움 보간 속도 (0이면 즉시 반영).</summary>
    [Min(0f)]
    [SerializeField] private float healthFillLerpSpeed = 14f;

    /// <summary>현재 장착 중인 무기 이름을 표시하는 텍스트.</summary>
    [Header("무기 및 탄창 HUD (우하단)")]
    [SerializeField] private Text weaponNameText;

    /// <summary>탄약 수치를 감싸는 컨테이너 (맨손 상태일 때 숨김).</summary>
    [SerializeField] private GameObject ammoGroup;

    /// <summary>현재 탄약 / 예비 탄약 또는 무한대 기호를 표시하는 텍스트.</summary>
    [SerializeField] private Text ammoCountText;

    /// <summary>맨손 상태일 때 우하단에 표시할 이름.</summary>
    [SerializeField] private string unarmedDisplayName = "Unarmed";

    /// <summary>근접무기 등 탄약을 소모하지 않는 무기의 탄창 표시 문자열.</summary>
    [SerializeField] private string unlimitedAmmoText = "∞";

    /// <summary>정상 탄약 상태의 텍스트 색상.</summary>
    [SerializeField] private Color normalAmmoColor = Color.white;

    /// <summary>현재 탄창이 0발일 때 강조할 텍스트 색상.</summary>
    [SerializeField] private Color emptyAmmoColor = new Color(1f, 0.38f, 0.38f, 1f);

    /// <summary>1~5번 순서대로 배치된 하단 장비 툴바 슬롯 배열 (5번은 추후 확장용).</summary>
    [Header("장비 툴바 HUD (하단 중앙)")]
    [SerializeField] private WeaponSlotUI[] toolbarSlots;

    /// <summary>현재 바인딩된 로컬 소유자 플레이어 컨트롤러.</summary>
    private PlayerController _boundPlayer;

    /// <summary>바인딩된 플레이어의 체력 컴포넌트.</summary>
    private PlayerHealth _playerHealth;

    /// <summary>바인딩된 플레이어의 무기 인벤토리 컴포넌트.</summary>
    private PlayerWeaponInventory _weaponInventory;

    /// <summary>바인딩된 플레이어의 무기 전투/탄약 컴포넌트.</summary>
    private PlayerWeaponController _weaponController;

    /// <summary>체력 바가 보간 중인 현재 표시 비율 (0~1).</summary>
    private float _displayedHealthRatio = 1f;

    /// <summary>체력 바가 도달해야 할 목표 비율 (0~1).</summary>
    private float _targetHealthRatio = 1f;

    /// <summary>체력 바 보간 갱신이 필요한지 여부.</summary>
    private bool _isHealthLerpActive;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (contentRoot == null)
        {
            contentRoot = gameObject;
        }

        if (hideWhenUnbound && _boundPlayer == null)
        {
            UpdateHudVisibility(false);
        }
    }

    private void OnEnable()
    {
        PlayerController.OnLocalPlayerStarted += HandleLocalPlayerStarted;
        PlayerController.OnLocalPlayerStopped += HandleLocalPlayerStopped;

        PlayerController localPlayer = PlayerController.LocalPlayer;
        if (localPlayer != null && localPlayer.IsSpawned && localPlayer.IsOwner)
        {
            BindPlayer(localPlayer);
        }
    }

    private void OnDisable()
    {
        PlayerController.OnLocalPlayerStarted -= HandleLocalPlayerStarted;
        PlayerController.OnLocalPlayerStopped -= HandleLocalPlayerStopped;
        UnbindPlayer();
    }

    private void Update()
    {
        if (!_isHealthLerpActive) return;

        float t = 1f - Mathf.Exp(-healthFillLerpSpeed * Time.unscaledDeltaTime);
        _displayedHealthRatio = Mathf.Lerp(_displayedHealthRatio, _targetHealthRatio, t);

        if (Mathf.Abs(_displayedHealthRatio - _targetHealthRatio) <= HealthSnapEpsilon)
        {
            _displayedHealthRatio = _targetHealthRatio;
            _isHealthLerpActive = false;
        }

        ApplyHealthVisual(_displayedHealthRatio);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>로컬 소유자 <paramref name="player"/>의 체력, 인벤토리, 탄약 이벤트를 HUD에 바인딩한다.</summary>
    public void BindPlayer(PlayerController player)
    {
        if (player == null || !player.IsOwner) return;

        UnbindPlayer();

        _boundPlayer = player;
        _playerHealth = player.GetComponent<PlayerHealth>();
        _weaponInventory = player.GetComponent<PlayerWeaponInventory>();
        _weaponController = player.GetComponent<PlayerWeaponController>();

        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged += HandleHealthChanged;
            _playerHealth.OnDeadStateChanged += HandleDeadStateChanged;
            RefreshHealth(_playerHealth.CurrentHealth, _playerHealth.MaxHealth, immediate: true);
        }

        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged += HandleEquippedWeaponChanged;
            _weaponInventory.OnSlotUpdated += HandleSlotUpdated;
        }

        if (_weaponController != null)
        {
            _weaponController.OnAmmoChanged += HandleAmmoChanged;
        }

        if (toolbarSlots != null)
        {
            for (int i = 0; i < toolbarSlots.Length; i++)
            {
                WeaponSlotUI slotUI = toolbarSlots[i];
                if (slotUI == null) continue;

                WeaponDataSO slotWeapon = _weaponInventory != null && i < PlayerWeaponInventory.SlotCount
                    ? _weaponInventory.GetWeaponDataInSlot((EWeaponSlotType)i)
                    : null;
                slotUI.SetWeapon(slotWeapon);
            }
        }

        EWeaponSlotType activeSlot = _weaponInventory != null ? _weaponInventory.ActiveSlot : EWeaponSlotType.Unarmed;
        UpdateToolbarFocus(activeSlot, immediate: true);
        RefreshActiveWeaponInfo();

        bool isDead = _playerHealth != null && _playerHealth.IsDead;
        UpdateHudVisibility(!(hideOnDeath && isDead));
    }

    /// <summary>현재 바인딩된 플레이어의 모든 이벤트 구독을 해제한다.</summary>
    public void UnbindPlayer()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= HandleHealthChanged;
            _playerHealth.OnDeadStateChanged -= HandleDeadStateChanged;
        }

        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged -= HandleEquippedWeaponChanged;
            _weaponInventory.OnSlotUpdated -= HandleSlotUpdated;
        }

        if (_weaponController != null)
        {
            _weaponController.OnAmmoChanged -= HandleAmmoChanged;
        }

        _boundPlayer = null;
        _playerHealth = null;
        _weaponInventory = null;
        _weaponController = null;
        _isHealthLerpActive = false;

        if (hideWhenUnbound)
        {
            UpdateHudVisibility(false);
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>로컬 소유자 플레이어가 스폰되면 HUD를 자동으로 연결한다.</summary>
    private void HandleLocalPlayerStarted(PlayerController player)
    {
        BindPlayer(player);
    }

    /// <summary>바인딩 중인 로컬 소유자 플레이어가 정지되면 HUD 연결을 해제한다.</summary>
    private void HandleLocalPlayerStopped(PlayerController player)
    {
        if (_boundPlayer == player)
        {
            UnbindPlayer();
        }
    }

    /// <summary>플레이어 체력 변경 이벤트를 체력 바와 수치 텍스트에 반영한다.</summary>
    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        RefreshHealth(currentHealth, maxHealth, immediate: false);
    }

    /// <summary>플레이어 사망/리스폰 상태 전환에 따라 HUD 표시 여부를 갱신한다.</summary>
    private void HandleDeadStateChanged(bool isDead)
    {
        if (!hideOnDeath) return;

        UpdateHudVisibility(!isDead);
    }

    /// <summary>장착 무기 변경 시 툴바 포커스와 우하단 무기/탄창 정보를 갱신한다.</summary>
    private void HandleEquippedWeaponChanged(EquippedWeaponState state)
    {
        if (toolbarSlots != null && _weaponInventory != null)
        {
            int slotIndex = (int)state.Slot;
            if (slotIndex >= 0 && slotIndex < toolbarSlots.Length && toolbarSlots[slotIndex] != null)
            {
                WeaponDataSO equippedData = state.Slot == EWeaponSlotType.Unarmed
                    ? null
                    : _weaponInventory.GetWeaponDataById(state.WeaponId);
                toolbarSlots[slotIndex].SetWeapon(equippedData);
            }
        }

        UpdateToolbarFocus(state.Slot, immediate: false);
        RefreshActiveWeaponInfo();
    }

    /// <summary>인벤토리 슬롯 변경 시 해당 툴바 슬롯 아이콘과 활성 무기 탄창을 갱신한다.</summary>
    private void HandleSlotUpdated(EWeaponSlotType slot, WeaponSlotState state)
    {
        if (toolbarSlots != null && _weaponInventory != null)
        {
            int slotIndex = (int)slot;
            if (slotIndex >= 0 && slotIndex < toolbarSlots.Length && toolbarSlots[slotIndex] != null)
            {
                WeaponDataSO weaponData = state.IsEmpty ? null : _weaponInventory.GetWeaponDataById(state.WeaponId);
                toolbarSlots[slotIndex].SetWeapon(weaponData);
            }
        }

        if (_weaponInventory != null && slot == _weaponInventory.ActiveSlot)
        {
            RefreshActiveWeaponInfo();
        }
    }

    /// <summary>현재 장착 슬롯의 탄약 변경 이벤트를 우하단 탄창 텍스트에 반영한다.</summary>
    private void HandleAmmoChanged(EWeaponType weaponType, int currentAmmo, int reserveAmmo)
    {
        if (_weaponInventory == null) return;
        if ((EWeaponSlotType)weaponType != _weaponInventory.ActiveSlot) return;

        UpdateAmmoDisplay(_weaponInventory.EquippedWeaponData, currentAmmo, reserveAmmo);
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>HUD 콘텐츠 루트의 활성 상태를 전환한다.</summary>
    private void UpdateHudVisibility(bool isVisible)
    {
        if (contentRoot != null && contentRoot.activeSelf != isVisible)
        {
            contentRoot.SetActive(isVisible);
        }
    }

    /// <summary>체력 수치 텍스트와 목표 체력 비율을 갱신한다.</summary>
    private void RefreshHealth(float currentHealth, float maxHealth, bool immediate)
    {
        float safeMax = Mathf.Max(1f, maxHealth);
        float clampedCurrent = Mathf.Clamp(currentHealth, 0f, safeMax);

        if (healthValueText != null)
        {
            healthValueText.text = $"{Mathf.CeilToInt(clampedCurrent)} / {Mathf.RoundToInt(safeMax)}";
        }

        _targetHealthRatio = clampedCurrent / safeMax;
        if (immediate || healthFillLerpSpeed <= 0f)
        {
            _displayedHealthRatio = _targetHealthRatio;
            _isHealthLerpActive = false;
            ApplyHealthVisual(_displayedHealthRatio);
            return;
        }

        _isHealthLerpActive = true;
    }

    /// <summary>정규화된 체력 비율 <paramref name="normalized"/>을 체력 바와 색상에 반영한다.</summary>
    private void ApplyHealthVisual(float normalized)
    {
        float clamped = Mathf.Clamp01(normalized);

        if (healthFillImage != null)
        {
            healthFillImage.enabled = clamped > 0.001f;
            healthFillImage.fillAmount = clamped;
            healthFillImage.color = clamped <= lowHealthThreshold ? lowHealthColor : fullHealthColor;
        }

        if (healthSlider != null)
        {
            healthSlider.normalizedValue = clamped;
        }
    }

    /// <summary>현재 장착 중인 무기의 이름과 탄창 정보를 우하단 패널에 반영한다.</summary>
    private void RefreshActiveWeaponInfo()
    {
        EWeaponSlotType activeSlot = _weaponInventory != null ? _weaponInventory.ActiveSlot : EWeaponSlotType.Unarmed;
        WeaponDataSO weaponData = activeSlot == EWeaponSlotType.Unarmed
            ? null
            : _weaponInventory.EquippedWeaponData;

        if (weaponNameText != null)
        {
            weaponNameText.text = weaponData != null && !string.IsNullOrEmpty(weaponData.WeaponName)
                ? weaponData.WeaponName
                : unarmedDisplayName;
        }

        int currentAmmo = 0;
        int reserveAmmo = 0;
        if (weaponData != null && weaponData.UsesAmmo)
        {
            if (_weaponController != null)
            {
                currentAmmo = _weaponController.CurrentAmmo;
                reserveAmmo = _weaponController.ReserveAmmo;
            }
            else if (_weaponInventory != null)
            {
                WeaponSlotState slotState = _weaponInventory.GetSlotState(activeSlot);
                currentAmmo = slotState.CurrentAmmo;
                reserveAmmo = slotState.ReserveAmmo;
            }
        }

        UpdateAmmoDisplay(weaponData, currentAmmo, reserveAmmo);
    }

    /// <summary>무기 유형(맨손, 비탄약 근접무기, 총기)에 맞춰 탄창 텍스트 표시와 색상을 갱신한다.</summary>
    private void UpdateAmmoDisplay(WeaponDataSO weaponData, int currentAmmo, int reserveAmmo)
    {
        if (weaponData == null)
        {
            if (ammoGroup != null)
            {
                ammoGroup.SetActive(false);
            }
            else if (ammoCountText != null)
            {
                ammoCountText.gameObject.SetActive(false);
            }
            return;
        }

        if (ammoGroup != null)
        {
            ammoGroup.SetActive(true);
        }

        if (ammoCountText == null) return;

        ammoCountText.gameObject.SetActive(true);

        if (!weaponData.UsesAmmo)
        {
            ammoCountText.text = unlimitedAmmoText;
            ammoCountText.color = normalAmmoColor;
            return;
        }

        if (weaponData.IsThrowable)
        {
            ammoCountText.text = $"x {currentAmmo}";
            ammoCountText.color = currentAmmo <= 0 ? emptyAmmoColor : normalAmmoColor;
            return;
        }

        ammoCountText.text = $"{currentAmmo} / {reserveAmmo}";
        ammoCountText.color = currentAmmo <= 0 ? emptyAmmoColor : normalAmmoColor;
    }

    /// <summary><paramref name="activeSlot"/>에 해당하는 툴바 슬롯에만 선택 포커스를 적용한다.</summary>
    private void UpdateToolbarFocus(EWeaponSlotType activeSlot, bool immediate)
    {
        if (toolbarSlots == null) return;

        int activeIndex = (int)activeSlot;
        for (int i = 0; i < toolbarSlots.Length; i++)
        {
            if (toolbarSlots[i] == null) continue;
            toolbarSlots[i].SetFocused(i == activeIndex, immediate);
        }
    }
}
