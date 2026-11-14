using System;
using UnityEngine;

/// <summary>프롭 클립과 손 클립의 조합마다 프롭을 하나씩 격자로 배치하고 재생함.</summary>
public class AnimationPreviewStage : MonoBehaviour
{
    public enum Stance
    {
        Any, // 모든 무기와 조합
        Aim, // 조준하는 무기와만 조합
        NoAim, // 조준하지 않는 무기와만 조합
        Unarmed // 무기와 조합하지 않음
    }

    [Serializable]
    private class PropClip
    {
        public AnimationClip clip;

        /// <summary>이 클립과 조합할 수 있는 무기의 종류.</summary>
        public Stance stance;

        /// <summary>이 클립과만 짝지어 재생하는 무기 없는 손 클립. 없으면 null.</summary>
        public AnimationClip pairedHandClip;
    }

    [Serializable]
    private class Prop
    {
        /// <summary><see cref="PlayerScript"/>와 <see cref="HandScript"/>를 가진 프롭 프리팹.</summary>
        public GameObject prefab;

        /// <summary>격자 한 칸의 크기 (x: -x 방향 열 간격, y: -z 방향 행 간격, 월드 단위).</summary>
        public Vector2 cellSize;

        /// <summary>프롭 위치에서 라벨까지의 오프셋 (스테이지 로컬 공간).</summary>
        public Vector3 labelOffset;

        /// <summary>한 클립이 격자의 한 행이 됨.</summary>
        public PropClip[] clips;
    }

    [Serializable]
    private class HandClip
    {
        public AnimationClip clip;

        /// <summary>이 손 클립과 함께 무기의 <see cref="Animator"/>에 재생하는 클립. 없으면 null.</summary>
        public AnimationClip weaponClip;

        /// <summary><see cref="weaponClip"/>이 시작하는 <see cref="clip"/>의 프레임.</summary>
        public int weaponClipFrame;
    }

    [Serializable]
    private class Weapon
    {
        public GameObject prefab;

        /// <summary>조준 애니메이션을 쓰는 무기인지 여부.</summary>
        public bool aims;

        /// <summary>오른손 본 기준 무기의 위치 (로컬 공간).</summary>
        public Vector3 gripPosition;

        /// <summary>오른손 본 기준 무기의 회전 (로컬 공간, 오일러 각).</summary>
        public Vector3 gripRotation;

        /// <summary>오른손 본 기준 무기의 스케일 (로컬 공간).</summary>
        public Vector3 gripScale;

        /// <summary>이 무기를 들고 재생하는 손 클립들.</summary>
        public HandClip[] handClips;

        /// <summary>
        /// 탄창을 교체하는 손 클립. <see cref="HandScript.EquipMagazineToGun"/> 이벤트를 가져야 함. 없으면 null.
        /// </summary>
        public AnimationClip reloadClip;
    }

    [SerializeField] private Prop[] props;

    [SerializeField] private Weapon[] weapons;

    /// <summary>조합 이름을 보여주는 라벨 프리팹.</summary>
    [SerializeField] private TextMesh labelPrefab;

    private void Start()
    {
        float rowPosition = 0f;

        foreach (Prop prop in props)
        {
            foreach (PropClip propClip in prop.clips)
            {
                int column = 0;

                foreach (Weapon weapon in weapons)
                {
                    if (propClip.stance == Stance.Unarmed ||
                        propClip.stance != Stance.Any && propClip.stance == Stance.Aim != weapon.aims)
                    {
                        continue;
                    }

                    foreach (HandClip handClip in weapon.handClips)
                    {
                        AnimationPreview preview = SpawnPreview(prop, propClip.clip, handClip.clip,
                            new Vector3(-column * prop.cellSize.x, 0f, rowPosition));
                        column++;

                        HandScript hand = preview.GetComponentInChildren<HandScript>();
                        GameObject weaponObject = Instantiate(weapon.prefab);
                        hand.SetRightHandItem(weaponObject);
                        weaponObject.transform.SetLocalPositionAndRotation(weapon.gripPosition,
                            Quaternion.Euler(weapon.gripRotation));
                        weaponObject.transform.localScale = weapon.gripScale;

                        if (handClip.weaponClip)
                        {
                            preview.PlayWeapon(weaponObject.GetComponent<Animator>(), handClip.weaponClip,
                                handClip.weaponClipFrame / handClip.clip.frameRate);
                        }

                        GunScript gun = weaponObject.GetComponent<GunScript>();

                        if (!gun || !weapon.reloadClip)
                        {
                            continue;
                        }

                        // 탄창을 끼우는 순간의 포즈에서 총의 탄창을 복제해, 왼손 탄창이 그 순간 총의 탄창과 겹치게 함
                        weapon.reloadClip.SampleAnimation(hand.gameObject, Array.Find(weapon.reloadClip.events,
                            animationEvent => animationEvent.functionName == nameof(HandScript.EquipMagazineToGun)).time);
                        GameObject magazine = Instantiate(gun.GetMagazine(), hand.GetLeftHand(), true);
                        magazine.SetActive(false);
                        hand.SetLeftHandItem(magazine);
                    }
                }

                if (propClip.pairedHandClip || propClip.stance == Stance.Unarmed)
                {
                    SpawnPreview(prop, propClip.clip, propClip.pairedHandClip,
                        new Vector3(-column * prop.cellSize.x, 0f, rowPosition));
                }

                rowPosition -= prop.cellSize.y;
            }
        }
    }

    /// <summary>프롭과 라벨을 <paramref name="localPosition"/>에 만들고 두 클립을 재생함.</summary>
    /// <param name="handClip">손 클립. null이면 프롭 클립만 재생함.</param>
    private AnimationPreview SpawnPreview(Prop prop, AnimationClip propClip, AnimationClip handClip,
        Vector3 localPosition)
    {
        GameObject propObject = Instantiate(prop.prefab, transform);
        propObject.transform.localPosition = localPosition;

        // FBX 클립 이름은 "Rig|Clip" 형식이라 '|' 뒤만 보여줌
        TextMesh label = Instantiate(labelPrefab, transform);
        label.transform.localPosition = localPosition + prop.labelOffset;
        label.text = propClip.name[(propClip.name.IndexOf('|') + 1)..];

        if (handClip)
        {
            label.text += " + " + handClip.name[(handClip.name.IndexOf('|') + 1)..];
        }

        propObject.name = label.text;

        AnimationPreview preview = propObject.AddComponent<AnimationPreview>();
        preview.Play(propClip, handClip);

        return preview;
    }
}
