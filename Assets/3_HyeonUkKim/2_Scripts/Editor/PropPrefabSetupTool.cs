using FishNet.Component.Animating;
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
        PlayerAnim propAnim = GetOrAddComponent<PlayerAnim>(root);
        NetworkObject netObj = GetOrAddComponent<NetworkObject>(root);
        NetworkAnimator netAnim = GetOrAddComponent<NetworkAnimator>(root);

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
            bool hasMoveX = false;
            for (int i = 0; i < ignoredParams.arraySize; i++)
            {
                if (ignoredParams.GetArrayElementAtIndex(i).stringValue == "MoveX")
                {
                    hasMoveX = true;
                    break;
                }
            }

            if (!hasMoveX)
            {
                int nextIndex = ignoredParams.arraySize;
                ignoredParams.InsertArrayElementAtIndex(nextIndex);
                ignoredParams.GetArrayElementAtIndex(nextIndex).stringValue = "MoveX";
            }
        }
        netAnimSerialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject controllerSerialized = new SerializedObject(controller);
        controllerSerialized.FindProperty("_groundCheck").objectReferenceValue = groundCheck;
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
}
