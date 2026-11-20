using FishNet.Component.Animating;
using FishNet.Component.Transforming;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

/// <summary>플레이어 프롭 프리팹에 필수 네트워크, 물리, 콜라이더 및 애니메이터 구성을 일괄 적용하는 에디터 도구.</summary>
public static class PropPrefabSetupTool
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const string DefaultBodyControllerPath = "Assets/3_HyeonUkKim/4_Animations/PlayerAnim.controller";
    private const string DefaultHandControllerPath = "Assets/3_HyeonUkKim/4_Animations/HandAnim.controller";
    private const string DefaultPlayerStatsPath = "Assets/3_HyeonUkKim/8_CombatData/DefaultPlayerStats.asset";
    private const string DefaultWeaponDatabasePath = "Assets/3_HyeonUkKim/8_CombatData/WeaponDatabase.asset";

    // ***************************************************************
    // Menu Actions
    // ***************************************************************

    /// <summary>프로젝트 창에서 선택한 <c>Assets/3_HyeonUkKim/</c> 프리팹에 프롭 플레이어 구성을 적용한다.</summary>
    [MenuItem("Tools/IamAProp/선택한 프롭 프리팹 자동 셋업")]
    public static void SetupSelectedPrefab()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            Debug.LogWarning("[PropPrefabSetupTool] Project 창에서 셋업할 프리팹을 먼저 선택하세요.");
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/3_HyeonUkKim/"))
        {
            Debug.LogWarning("[PropPrefabSetupTool] Assets/3_HyeonUkKim/ 내부의 프리팹 에셋만 수정할 수 있습니다.");
            return;
        }

        using PrefabUtility.EditPrefabContentsScope editScope = new PrefabUtility.EditPrefabContentsScope(assetPath);
        ApplyToRoot(editScope.prefabContentsRoot, propData: null);
        Debug.Log($"[PropPrefabSetupTool] 프롭 프리팹 셋업 완료: {assetPath}");
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="root"/> 게임 오브젝트에 프롭 플레이어 컴포넌트, 콜라이더, 기준 자식 및 애니메이터를 설정한다.</summary>
    public static void ApplyToRoot(GameObject root, PropDataSO propData)
    {
        if (root == null) return;

        Rigidbody rb = GetOrAddComponent<Rigidbody>(root);
        rb.mass = propData != null ? propData.RigidbodyMass : 3f;
        rb.linearDamping = propData != null ? propData.RigidbodyLinearDamping : 1f;
        rb.angularDamping = propData != null ? propData.RigidbodyAngularDamping : 0.1f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        Animator bodyAnimator = root.GetComponent<Animator>();
        if (bodyAnimator == null)
        {
            bodyAnimator = root.GetComponentInChildren<Animator>(true);
        }

        RuntimeAnimatorController baseBodyController = propData != null && propData.PropTemplate != null
            ? propData.PropTemplate.BaseBodyController
            : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DefaultBodyControllerPath);
        if (bodyAnimator != null && bodyAnimator.runtimeAnimatorController == null && baseBodyController != null)
        {
            bodyAnimator.runtimeAnimatorController = baseBodyController;
            bodyAnimator.applyRootMotion = false;
        }

        HandScript handScript = root.GetComponentInChildren<HandScript>(true);
        PlayerScript playerScript = root.GetComponentInChildren<PlayerScript>(true);
        if (handScript != null && playerScript != null)
        {
            SerializedObject handSerialized = new SerializedObject(handScript);
            handSerialized.FindProperty("player").objectReferenceValue = playerScript;
            handSerialized.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject playerSerialized = new SerializedObject(playerScript);
            playerSerialized.FindProperty("hand").objectReferenceValue = handScript;
            playerSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        Animator handAnimator = handScript != null ? handScript.GetComponent<Animator>() : null;
        RuntimeAnimatorController baseHandController = propData != null && propData.PropTemplate != null
            ? propData.PropTemplate.BaseHandController
            : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DefaultHandControllerPath);
        if (handAnimator != null && handAnimator.runtimeAnimatorController == null && baseHandController != null)
        {
            handAnimator.runtimeAnimatorController = baseHandController;
        }

        float propHeight = 0.5f;
        Renderer bodyRenderer = null;
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.name != "HandsMesh")
            {
                bodyRenderer = smr;
                break;
            }
        }
        if (bodyRenderer == null)
        {
            bodyRenderer = root.GetComponentInChildren<MeshRenderer>(true);
        }

        if (bodyRenderer != null)
        {
            propHeight = bodyRenderer.localBounds.size.y;
        }

        if (root.GetComponentInChildren<Collider>(true) == null)
        {
            BoxCollider box = root.AddComponent<BoxCollider>();
            if (bodyRenderer != null)
            {
                Bounds localBounds = bodyRenderer.localBounds;
                Vector3 size = localBounds.size;
                box.center = new Vector3(localBounds.center.x, size.y * 0.5f, localBounds.center.z);
                box.size = size;
            }
            else
            {
                box.center = new Vector3(0f, 0.25f, 0f);
                box.size = new Vector3(0.5f, 0.5f, 0.5f);
            }
        }

        Transform groundCheck = EnsureChildTransform(root.transform, "GroundCheck", Vector3.zero);
        EnsureChildTransform(root.transform, "TpsViewPos", new Vector3(0f, Mathf.Max(0.35f, propHeight * 0.85f), 0f));

        PlayerController controller = GetOrAddComponent<PlayerController>(root);
        PlayerWeaponInventory weaponInventory = GetOrAddComponent<PlayerWeaponInventory>(root);
        PlayerAnim propAnim = GetOrAddComponent<PlayerAnim>(root);
        PlayerHealth playerHealth = GetOrAddComponent<PlayerHealth>(root);
        PlayerWeaponController weaponController = GetOrAddComponent<PlayerWeaponController>(root);
        NetworkObject netObj = GetOrAddComponent<NetworkObject>(root);
        NetworkAnimator netAnim = GetOrAddComponent<NetworkAnimator>(root);

        WeaponDatabaseSO defaultWeaponDb = AssetDatabase.LoadAssetAtPath<WeaponDatabaseSO>(DefaultWeaponDatabasePath);
        if (defaultWeaponDb != null)
        {
            SerializedObject invSerialized = new SerializedObject(weaponInventory);
            SerializedProperty dbProp = invSerialized.FindProperty("weaponDatabase");
            if (dbProp != null && dbProp.objectReferenceValue == null)
            {
                dbProp.objectReferenceValue = defaultWeaponDb;
                invSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            root.layer = playerLayer;
        }

        int combatMask = LayerMask.GetMask("Default", "Ground", "Player", "Hitbox");
        if (combatMask != 0)
        {
            SerializedObject weaponSerialized = new SerializedObject(weaponController);
            weaponSerialized.FindProperty("hitLayerMask").intValue = combatMask;
            weaponSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        PlayerStatsSO defaultStats = AssetDatabase.LoadAssetAtPath<PlayerStatsSO>(DefaultPlayerStatsPath);
        if (defaultStats != null)
        {
            SerializedObject healthSerialized = new SerializedObject(playerHealth);
            SerializedProperty statsProp = healthSerialized.FindProperty("playerStats");
            if (statsProp != null && statsProp.objectReferenceValue == null)
            {
                statsProp.objectReferenceValue = defaultStats;
                healthSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        foreach (NetworkTransform staleTransform in root.GetComponentsInChildren<NetworkTransform>(true))
        {
            Object.DestroyImmediate(staleTransform);
        }

        SerializedObject netObjSerialized = new SerializedObject(netObj);
        netObjSerialized.FindProperty("_enablePrediction").boolValue = true;
        netObjSerialized.FindProperty("_predictionType").intValue = 1; // PredictionType.Rigidbody
        netObjSerialized.FindProperty("_localReconcileCorrectionType").intValue = 2; // TransformAndVelocities
        netObjSerialized.FindProperty("_enableStateForwarding").boolValue = true;
        netObjSerialized.FindProperty("_isSpawnable").boolValue = true;
        netObjSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject netAnimSerialized = new SerializedObject(netAnim);
        netAnimSerialized.FindProperty("_animator").objectReferenceValue = bodyAnimator;
        netAnimSerialized.FindProperty("_clientAuthoritative").boolValue = true;
        netAnimSerialized.FindProperty("_smoothFloats").boolValue = true;
        netAnimSerialized.FindProperty("_interpolation").intValue = 2;
        SerializedProperty ignoredParams = netAnimSerialized.FindProperty("IgnoredParameters");
        if (ignoredParams != null)
        {
            EnsureIgnoredParameter(ignoredParams, "MoveX");
            EnsureIgnoredParameter(ignoredParams, "MoveZ");
            EnsureIgnoredParameter(ignoredParams, "Hit");
            EnsureIgnoredParameter(ignoredParams, "HitIndex");
        }
        netAnimSerialized.ApplyModifiedPropertiesWithoutUndo();

        Transform aimPivot = null;
        if (handScript != null)
        {
            Transform handsTf = handScript.transform;
            if (handsTf.parent != null && handsTf.parent.name == "AimPivot")
            {
                aimPivot = handsTf.parent;
            }
            else if (handsTf.parent != null)
            {
                if (PrefabUtility.IsPartOfPrefabInstance(root))
                {
                    PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                }

                Transform parentBone = handsTf.parent;
                Vector3 savedPos = handsTf.localPosition;
                Quaternion savedRot = handsTf.localRotation;
                Vector3 savedScale = handsTf.localScale;

                GameObject aimPivotObject = new GameObject("AimPivot");
                aimPivot = aimPivotObject.transform;
                aimPivot.SetParent(parentBone, false);
                aimPivot.localPosition = savedPos;
                aimPivot.localRotation = Quaternion.identity;
                aimPivot.localScale = Vector3.one;

                handsTf.SetParent(aimPivot, false);
                handsTf.localPosition = Vector3.zero;
                handsTf.localRotation = savedRot;
                handsTf.localScale = savedScale;
            }

            HandReloadAudioRelay reloadRelay = GetOrAddComponent<HandReloadAudioRelay>(handsTf.gameObject);
            SerializedObject relaySerialized = new SerializedObject(reloadRelay);
            relaySerialized.FindProperty("weaponController").objectReferenceValue = weaponController;
            relaySerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        GameObject audioHost = aimPivot != null ? aimPivot.gameObject : root;
        AudioSource weaponAudioSource = GetOrAddComponent<AudioSource>(audioHost);
        weaponAudioSource.playOnAwake = false;
        weaponAudioSource.loop = false;
        weaponAudioSource.spatialBlend = 1f;
        weaponAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        weaponAudioSource.minDistance = 2f;
        weaponAudioSource.maxDistance = 40f;
        weaponAudioSource.dopplerLevel = 0f;

        SerializedObject weaponAudioSerialized = new SerializedObject(weaponController);
        weaponAudioSerialized.FindProperty("weaponAudioSource").objectReferenceValue = weaponAudioSource;
        weaponAudioSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject controllerSerialized = new SerializedObject(controller);
        controllerSerialized.FindProperty("_groundCheck").objectReferenceValue = groundCheck;
        NetworkTickSmoother smoother = root.GetComponentInChildren<NetworkTickSmoother>(true);
        if (smoother != null)
        {
            controllerSerialized.FindProperty("visualRoot").objectReferenceValue = smoother.transform;
        }
        if (aimPivot != null)
        {
            controllerSerialized.FindProperty("aimPitchTarget").objectReferenceValue = aimPivot;
        }
        if (propData != null)
        {
            controllerSerialized.FindProperty("propData").objectReferenceValue = propData;
        }
        controllerSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject animSerialized = new SerializedObject(propAnim);
        animSerialized.FindProperty("bodyAnimator").objectReferenceValue = bodyAnimator;
        animSerialized.FindProperty("handAnimator").objectReferenceValue = handAnimator;
        animSerialized.FindProperty("handScript").objectReferenceValue = handScript;
        if (propData != null)
        {
            animSerialized.FindProperty("propData").objectReferenceValue = propData;
        }
        animSerialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary><paramref name="target"/>에서 <typeparamref name="T"/> 컴포넌트를 조회하거나 없으면 추가한다.</summary>
    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    /// <summary><paramref name="parent"/> 아래에서 <paramref name="childName"/> 트랜스폼을 찾거나 지정 로컬 위치로 생성한다.</summary>
    private static Transform EnsureChildTransform(Transform parent, string childName, Vector3 defaultLocalPosition)
    {
        Transform child = parent.Find(childName);
        if (child != null) return child;

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        childObject.transform.localPosition = defaultLocalPosition;
        return childObject.transform;
    }

    /// <summary><see cref="NetworkAnimator"/>의 <c>IgnoredParameters</c> 배열에 <paramref name="paramName"/>이 없으면 추가한다.</summary>
    private static void EnsureIgnoredParameter(SerializedProperty ignoredParams, string paramName)
    {
        for (int i = 0; i < ignoredParams.arraySize; i++)
        {
            if (ignoredParams.GetArrayElementAtIndex(i).stringValue == paramName)
            {
                return;
            }
        }

        int nextIndex = ignoredParams.arraySize;
        ignoredParams.InsertArrayElementAtIndex(nextIndex);
        ignoredParams.GetArrayElementAtIndex(nextIndex).stringValue = paramName;
    }
}
