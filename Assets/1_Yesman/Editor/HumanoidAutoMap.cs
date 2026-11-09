using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Unity's Humanoid auto-mapper picks the top-most bone that has the spine and both
// legs under it as "Hips". Our rigs have an extra "root" bone above Hips, so it maps
// Hips=root and the avatar fails ("Required human bone ... not found").
// This maps human bones by name instead, on every (re)import of a model in Folder.
// It needs one extra reimport: the skeleton pose is only known after the first import.
public class HumanoidAutoMap : AssetPostprocessor
{
    const string Folder = "Assets/1_Yesman/Models/";

    public override uint GetVersion() { return 1; }

    // Run after ResetToBindPose so the stored skeleton is the bind (T-)pose.
    public override int GetPostprocessOrder() { return 100; }

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (ModelImporter)assetImporter;
        if (importer.animationType != ModelImporterAnimationType.Human) return;
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) return;

        var human = BuildHumanMap(root.transform);
        if (human.Count == 0) return; // not one of our named rigs; leave it to Unity

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

        // Importer settings can't be changed mid-import; apply them once the import finishes.
        Pending[assetPath] = (human.ToArray(), skeleton);
    }

    static readonly Dictionary<string, (HumanBone[] human, SkeletonBone[] skeleton)> Pending =
        new Dictionary<string, (HumanBone[], SkeletonBone[])>();

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

    static List<HumanBone> BuildHumanMap(Transform root)
    {
        // "Left Thumb Proximal" -> "LeftThumbProximal", "LeftUpperLeg" -> "LeftUpperLeg", ...
        var byName = new Dictionary<string, string>();
        foreach (var h in HumanTrait.BoneName) byName[h.Replace(" ", "")] = h;

        // Rig-specific aliases: 3-segment Thumb and Mitten (mapped to Index) fingers.
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

        // Only take over when every required bone was found by name.
        var required = Enumerable.Range(0, HumanTrait.BoneCount).Where(HumanTrait.RequiredBone).Select(i => HumanTrait.BoneName[i]);
        return required.All(used.Contains) ? result : new List<HumanBone>();
    }
}
