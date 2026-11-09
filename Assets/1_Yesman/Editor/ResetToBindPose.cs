using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Unity builds an animated FBX's default pose from frame 0 of its FIRST take
// (e.g. Blender's "Aim" action). This restores every skinned bone to the mesh's
// bind pose (= Blender rest / T-pose) after import. Animation clips are unaffected.
public class ResetToBindPose : AssetPostprocessor
{
    const string Folder = "Assets/1_Yesman/Models/";

    public override uint GetVersion() { return 1; }

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

            var target = new Dictionary<Transform, Matrix4x4>();
            for (int i = 0; i < bones.Length && i < bind.Length; i++)
                if (bones[i] != null) target[bones[i]] = meshL2W * bind[i].inverse;

            foreach (var kv in target)
            {
                Transform b = kv.Key, p = b.parent;
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