using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="Folder"/> 안의 모델을 임포트할 때마다 스킨 본을 메시의 바인드 포즈(블렌더 레스트 포즈 / T-포즈)로 되돌린다.
///
/// Unity는 애니메이션이 있는 FBX의 기본 포즈를 첫 번째 테이크(예: 블렌더의 "Aim" 액션)의 0번 프레임으로 만든다.
/// 임포트 설정으로는 이 포즈를 바꿀 수 없다. 애니메이션 클립은 영향을 받지 않는다.
/// </summary>
public class ResetToBindPose : AssetPostprocessor
{
    /// <summary>처리할 모델이 있는 폴더 (프로젝트 기준 경로).</summary>
    const string Folder = "Assets/";

    public override uint GetVersion() { return 1; }

    /// <summary>스킨 본마다 바인드 시점의 월드 행렬을 부모 기준 로컬 트랜스폼으로 바꿔 적용한다.</summary>
    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(Folder)) return;

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var bones = smr.bones;
            var bind = mesh.bindposes;
            var meshL2W = smr.transform.localToWorldMatrix;

            // 본별 바인드 시점의 월드 행렬
            var target = new Dictionary<Transform, Matrix4x4>();
            for (int i = 0; i < bones.Length && i < bind.Length; i++)
                if (bones[i] != null) target[bones[i]] = meshL2W * bind[i].inverse;

            foreach (var kv in target)
            {
                Transform b = kv.Key, p = b.parent;
                // 부모가 스킨 본이면 바인드 시점의 월드 행렬, 아니면 현재 월드 행렬
                Matrix4x4 parentW = (p != null && target.ContainsKey(p)) ? target[p]
                                  : (p != null ? p.localToWorldMatrix : Matrix4x4.identity);
                Matrix4x4 local = parentW.inverse * kv.Value;
                b.localPosition = local.GetColumn(3);
                b.localRotation = local.rotation;
                b.localScale    = local.lossyScale;
            }
        }
    }
}
