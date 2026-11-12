using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="Folder"/> 안의 모델을 (재)임포트할 때마다 휴먼 본을 이름으로 매핑한다.
///
/// Unity의 Humanoid 자동 매핑은 척추와 양쪽 다리를 모두 자식으로 가진 가장 위쪽 본을 "Hips"로 고른다.
/// 우리 리그는 Hips 위에 "root" 본이 하나 더 있어서 Hips=root로 매핑되고 아바타 생성이 실패한다
/// ("Required human bone ... not found").
/// 스켈레톤 포즈는 첫 임포트가 끝나야 알 수 있으므로 재임포트가 한 번 더 필요하다.
/// </summary>
public class HumanoidAutoMap : AssetPostprocessor
{
    /// <summary>처리할 모델이 있는 폴더 (프로젝트 기준 경로).</summary>
    const string Folder = "Assets/";

    public override uint GetVersion() { return 1; }

    public override int GetPostprocessOrder() { return 100; } // ResetToBindPose (0) 이후

    /// <summary>이름으로 만든 휴먼 본 매핑이 임포터의 현재 매핑과 다르면 <see cref="Pending"/>에 넣는다.</summary>
    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (ModelImporter)assetImporter;
        if (importer.animationType != ModelImporterAnimationType.Human) return;
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) return;

        var human = BuildHumanMap(root.transform);
        if (human.Count == 0) return; // 우리 이름 규칙의 리그가 아니면 Unity에 맡긴다

        var current = importer.humanDescription;
        bool sameMap = current.human != null && current.skeleton != null && current.skeleton.Length > 0
            && current.human.Length == human.Count
            && current.human.All(h => human.Any(n => n.humanName == h.humanName && n.boneName == h.boneName));
        if (sameMap) return;

        var skeleton = root.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone
        {
            name = t.name,
            position = t.localPosition,
            rotation = t.localRotation,
            scale = t.localScale,
        }).ToArray();

        Pending[assetPath] = (human.ToArray(), skeleton);
    }

    /// <summary>
    /// 임포트가 끝난 뒤 임포터에 적용할 휴먼 본 매핑과 스켈레톤 (키: 에셋 경로).
    /// 임포트 도중에는 임포터 설정을 바꿀 수 없다.
    /// </summary>
    static readonly Dictionary<string, (HumanBone[] human, SkeletonBone[] skeleton)> Pending =
        new Dictionary<string, (HumanBone[], SkeletonBone[])>();

    /// <summary><see cref="Pending"/>의 매핑을 임포터에 저장하고 다시 임포트한다.</summary>
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (Pending.Count == 0) return;
        var work = Pending.ToList();
        Pending.Clear();
        foreach (var kv in work)
        {
            var imp = AssetImporter.GetAtPath(kv.Key) as ModelImporter;
            if (imp == null) continue;
            var hd = imp.humanDescription;
            hd.human = kv.Value.human;
            hd.skeleton = kv.Value.skeleton;
            imp.humanDescription = hd;
            imp.SaveAndReimport();
            Debug.Log($"[HumanoidAutoMap] Mapped {kv.Value.human.Length} human bones by name for {kv.Key}");
        }
    }

    /// <summary>본 이름으로 휴먼 본 매핑을 만든다.</summary>
    /// <returns>필수 휴먼 본을 모두 찾았으면 매핑 목록, 하나라도 없으면 빈 목록.</returns>
    static List<HumanBone> BuildHumanMap(Transform root)
    {
        // 공백을 뺀 휴먼 본 이름으로 찾는다 ("Left Thumb Proximal" -> "LeftThumbProximal")
        var byName = new Dictionary<string, string>();
        foreach (var h in HumanTrait.BoneName) byName[h.Replace(" ", "")] = h;

        // 리그 전용 별칭: 3마디 Thumb, Index로 매핑하는 Mitten
        string[] seg = { "Proximal", "Intermediate", "Distal" };
        foreach (var side in new[] { "Left", "Right" })
            for (int i = 0; i < 3; i++)
            {
                byName[$"{side}Thumb{i + 1}"] = $"{side} Thumb {seg[i]}";
                byName[$"{side}Mitten{i + 1}"] = $"{side} Index {seg[i]}";
            }

        var result = new List<HumanBone>();
        var used = new HashSet<string>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root || !byName.TryGetValue(t.name, out var humanName) || !used.Add(humanName)) continue;
            result.Add(new HumanBone { humanName = humanName, boneName = t.name, limit = new HumanLimit { useDefaultValues = true } });
        }

        var required = Enumerable.Range(0, HumanTrait.BoneCount).Where(HumanTrait.RequiredBone).Select(i => HumanTrait.BoneName[i]);
        return required.All(used.Contains) ? result : new List<HumanBone>();
    }
}
