using System.Collections.Generic;
using DriftCareer.Core.Character;
using DriftCareer.Runtime.Track;
using UnityEngine;

namespace DriftCareer.Runtime.Character
{
    public enum CharacterPose { Standing, Seated }

    /// <summary>
    /// Procedural stylized driver built from primitives: proportions from body type/height, skin, hair, face,
    /// and gear per slot (head, top, bottom, gloves, shoes, accessory) with two colors. Rebuilt on every change
    /// (cheap: ~40 renderers, shared cached meshes). Original work - no third-party assets.
    /// </summary>
    public sealed class CharacterRig : MonoBehaviour
    {
        private readonly List<GameObject> _parts = new List<GameObject>();
        private GameObject _customModel;
        public CharacterPose Pose { get; private set; }
        public Transform Head { get; private set; }

        public static CharacterRig Create(Transform parent, CharacterPose pose)
        {
            var go = new GameObject(pose == CharacterPose.Seated ? "Driver" : "Pilot");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<CharacterRig>();
            rig.Pose = pose;
            return rig;
        }

        private static Material Mat(string hex, float smooth = 0.35f, float metal = 0f)
            => MaterialLibrary.LitColor("chr_" + hex + "_" + smooth, MaterialLibrary.ParseHex(hex, Color.gray), smooth, metal);

        private Transform Box(string name, Transform parent, Vector3 center, Vector3 size, string hex, float smooth = 0.35f, Quaternion? rot = null)
        {
            var go = ProceduralMeshes.Spawn(name, parent, ProceduralMeshes.Box(size), Mat(hex, smooth), center - new Vector3(0f, size.y * 0.5f, 0f), rot ?? Quaternion.identity);
            _parts.Add(go);
            return go.transform;
        }

        private Transform Ball(string name, Transform parent, Vector3 center, float radius, string hex, bool hemi = false, float smooth = 0.4f)
        {
            var go = ProceduralMeshes.Spawn(name, parent, ProceduralMeshes.Sphere(radius, 14, hemi), Mat(hex, smooth), center, Quaternion.identity);
            _parts.Add(go);
            return go.transform;
        }

        private Transform Joint(string name, Transform parent, Vector3 local, Vector3 euler)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            t.localRotation = Quaternion.Euler(euler);
            _parts.Add(t.gameObject);
            return t;
        }

        /// <summary>Optional imported model (standing preview only; the seated driver stays procedural).</summary>
        public void UseCustomModel(GameObject prefab, float heightM)
        {
            Clear();
            if (prefab == null) return;
            _customModel = Instantiate(prefab, transform);
            var rs = _customModel.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float h = Mathf.Max(0.1f, b.size.y);
            _customModel.transform.localScale *= heightM / h;
        }

        private void Clear()
        {
            foreach (var p in _parts) if (p != null) Destroy(p);
            _parts.Clear();
            if (_customModel != null) Destroy(_customModel);
            _customModel = null;
        }

        public void Build(CharacterAppearance a)
        {
            Clear();
            a = CharacterCatalog.Sanitize(a);
            bool seated = Pose == CharacterPose.Seated;
            float h = a.height * CharacterCatalog.BaseHeightM / 1.78f;
            float w = a.body == BodyType.Slim ? 0.88f : (a.body == BodyType.Strong ? 1.15f : 1f);
            var top = a.Gear(GearSlot.Top); var topItem = CharacterCatalog.Find(top.itemId);
            var bottom = a.Gear(GearSlot.Bottom); var bottomItem = CharacterCatalog.Find(bottom.itemId);
            var gloves = a.Gear(GearSlot.Gloves); var glovesItem = CharacterCatalog.Find(gloves.itemId);
            var shoes = a.Gear(GearSlot.Shoes);
            var head = a.Gear(GearSlot.Head); var headItem = CharacterCatalog.Find(head.itemId);
            var acc = CharacterCatalog.Find(a.Gear(GearSlot.Accessory).itemId);
            string skin = a.skinTone;
            int topStyle = topItem?.style ?? 0, bottomStyle = bottomItem?.style ?? 0;
            bool longSleeves = topStyle != 0;
            bool shortsLegs = bottomStyle == 3;
            string handColor = (glovesItem?.style ?? 0) > 0 ? gloves.primary : skin;

            // Modelled proportions are ~2.0 m tall; 0.885 brings the default avatar to 1.78 m.
            const float modelScale = 0.885f;
            var root = Joint("Root", transform, Vector3.zero, Vector3.zero);
            root.localScale = new Vector3(h * w, h, h * w) * modelScale;
            float hipY = seated ? 0.0f : 0.98f;
            var pelvis = Joint("Pelvis", root, new Vector3(0f, hipY, 0f), Vector3.zero);
            Box("Hips", pelvis, new Vector3(0f, 0.08f, 0f), new Vector3(0.34f, 0.18f, 0.2f), bottom.primary, 0.3f);

            // Legs.
            for (int s = -1; s <= 1; s += 2)
            {
                var hip = Joint(s < 0 ? "HipL" : "HipR", pelvis, new Vector3(0.1f * s, 0f, 0f), seated ? new Vector3(-88f, 0f, 0f) : Vector3.zero);
                Box("Thigh", hip, new Vector3(0f, -0.0f, 0f), new Vector3(0.15f, 0.45f, 0.16f), bottom.primary, 0.3f, Quaternion.identity).localPosition = new Vector3(0f, -0.45f, 0f);
                if (bottomStyle == 1) Box("Pocket", hip, new Vector3(0.08f * s, -0.22f, 0f), new Vector3(0.04f, 0.12f, 0.12f), bottom.secondary);
                if (bottomStyle == 2) Box("Stripe", hip, new Vector3(0.076f * s, -0.02f, 0f), new Vector3(0.012f, 0.44f, 0.05f), bottom.secondary);
                var knee = Joint("Knee", hip, new Vector3(0f, -0.45f, 0f), seated ? new Vector3(70f, 0f, 0f) : Vector3.zero);
                Box("Shin", knee, Vector3.zero, new Vector3(0.13f, 0.44f, 0.14f), shortsLegs ? skin : bottom.primary, 0.3f).localPosition = new Vector3(0f, -0.44f, 0f);
                var ankle = Joint("Ankle", knee, new Vector3(0f, -0.44f, 0f), seated ? new Vector3(18f, 0f, 0f) : Vector3.zero);
                Box("Shoe", ankle, new Vector3(0f, 0f, 0.05f), new Vector3(0.13f, 0.09f, 0.28f), shoes.primary, 0.5f).localPosition = new Vector3(0f, -0.09f, 0.05f);
                Box("Sole", ankle, Vector3.zero, new Vector3(0.135f, 0.025f, 0.29f), shoes.secondary).localPosition = new Vector3(0f, -0.1f, 0.05f);
            }

            // Torso.
            var chest = Joint("Chest", pelvis, new Vector3(0f, 0.17f, 0f), seated ? new Vector3(-12f, 0f, 0f) : Vector3.zero);
            Box("Torso", chest, Vector3.zero, new Vector3(0.4f, 0.56f, 0.24f), top.primary, topStyle == 3 ? 0.55f : 0.3f).localPosition = Vector3.zero;
            if (topStyle == 3)
            {
                for (int s = -1; s <= 1; s += 2) Box("SuitStripe", chest, Vector3.zero, new Vector3(0.03f, 0.56f, 0.245f), top.secondary).localPosition = new Vector3(0.17f * s, 0f, 0f);
                Box("Collar", chest, Vector3.zero, new Vector3(0.24f, 0.06f, 0.2f), top.secondary).localPosition = new Vector3(0f, 0.55f, 0f);
            }
            else if (topStyle == 1) Box("Hood", chest, Vector3.zero, new Vector3(0.3f, 0.2f, 0.1f), top.primary).localPosition = new Vector3(0f, 0.45f, -0.14f);
            else if (topStyle == 2) { Box("Zip", chest, Vector3.zero, new Vector3(0.02f, 0.52f, 0.01f), top.secondary).localPosition = new Vector3(0f, 0.02f, 0.122f); }
            else if (topStyle == 4) Box("Bib", chest, Vector3.zero, new Vector3(0.3f, 0.3f, 0.01f), top.secondary).localPosition = new Vector3(0f, 0.05f, 0.122f);
            else Box("Print", chest, Vector3.zero, new Vector3(0.18f, 0.12f, 0.01f), top.secondary).localPosition = new Vector3(0f, 0.3f, 0.122f);

            // Arms (seated: reach forward to the steering wheel).
            for (int s = -1; s <= 1; s += 2)
            {
                var shoulder = Joint(s < 0 ? "ShoulderL" : "ShoulderR", chest, new Vector3(0.25f * s, 0.52f, 0f), seated ? new Vector3(-62f, 0f, -8f * s) : new Vector3(0f, 0f, 6f * s));
                Box("UpperArm", shoulder, Vector3.zero, new Vector3(0.11f, 0.3f, 0.12f), top.primary, 0.3f).localPosition = new Vector3(0f, -0.3f, 0f);
                var elbow = Joint("Elbow", shoulder, new Vector3(0f, -0.3f, 0f), seated ? new Vector3(-30f, 0f, 0f) : new Vector3(-6f, 0f, 0f));
                Box("Forearm", elbow, Vector3.zero, new Vector3(0.1f, 0.27f, 0.1f), longSleeves ? top.primary : skin, 0.3f).localPosition = new Vector3(0f, -0.27f, 0f);
                Ball("Hand", elbow, new Vector3(0f, -0.31f, 0f), 0.055f, handColor);
            }

            // Head.
            var neck = Joint("Neck", chest, new Vector3(0f, 0.58f, 0f), Vector3.zero);
            Box("NeckSkin", neck, Vector3.zero, new Vector3(0.09f, 0.1f, 0.09f), skin).localPosition = new Vector3(0f, 0.1f, 0f);
            var headT = Joint("Head", neck, new Vector3(0f, 0.22f, 0f), Vector3.zero);
            Head = headT;
            Ball("Skull", headT, Vector3.zero, 0.12f, skin, false, 0.45f);
            float eyeSpace = 0.04f + a.face * 0.004f;
            for (int s = -1; s <= 1; s += 2)
            {
                Box("Eye", headT, Vector3.zero, new Vector3(0.025f, 0.018f, 0.01f), "#141414").localPosition = new Vector3(eyeSpace * s, 0.01f, 0.112f);
                if (a.face >= 2) Box("Brow", headT, Vector3.zero, new Vector3(0.035f, 0.01f, 0.01f), a.hairColor).localPosition = new Vector3(eyeSpace * s, 0.045f, 0.112f);
            }
            if (a.face == 3) Box("Beard", headT, Vector3.zero, new Vector3(0.14f, 0.06f, 0.04f), a.hairColor).localPosition = new Vector3(0f, -0.09f, 0.08f);
            int headStyle = headItem?.style ?? 0;
            if (headStyle < 3) BuildHair(headT, a);
            switch (headStyle)
            {
                case 1:
                    Ball("Cap", headT, new Vector3(0f, 0.02f, 0f), 0.13f, head.primary, true);
                    Box("Brim", headT, Vector3.zero, new Vector3(0.2f, 0.015f, 0.12f), head.secondary).localPosition = new Vector3(0f, 0.04f, 0.15f);
                    break;
                case 2: Ball("Beanie", headT, new Vector3(0f, 0.03f, 0f), 0.135f, head.primary, true); break;
                case 3:
                    Ball("Helmet", headT, new Vector3(0f, 0.01f, -0.01f), 0.165f, head.primary, false, 0.85f);
                    Box("HelmetStripe", headT, Vector3.zero, new Vector3(0.04f, 0.02f, 0.3f), head.secondary).localPosition = new Vector3(0f, 0.165f, -0.01f);
                    break;
                case 4:
                    Ball("Helmet", headT, new Vector3(0f, 0.01f, -0.01f), 0.17f, head.primary, false, 0.85f);
                    Box("Visor", headT, Vector3.zero, new Vector3(0.22f, 0.07f, 0.03f), "#0A0C10", 0.95f).localPosition = new Vector3(0f, 0.02f, 0.155f);
                    Box("HelmetStripe", headT, Vector3.zero, new Vector3(0.05f, 0.02f, 0.32f), head.secondary).localPosition = new Vector3(0f, 0.172f, -0.01f);
                    break;
            }
            switch (acc?.style ?? 0)
            {
                case 1: if (headStyle != 4) Box("Glasses", headT, Vector3.zero, new Vector3(0.14f, 0.03f, 0.02f), "#0A0A0A", 0.9f).localPosition = new Vector3(0f, 0.025f, 0.118f); break;
                case 2: Box("Hans", chest, Vector3.zero, new Vector3(0.36f, 0.06f, 0.26f), "#141414", 0.6f).localPosition = new Vector3(0f, 0.6f, -0.02f); break;
                case 3:
                    if (headStyle < 3)
                    {
                        Box("Band", headT, Vector3.zero, new Vector3(0.27f, 0.025f, 0.03f), a.Gear(GearSlot.Accessory).primary).localPosition = new Vector3(0f, 0.12f, 0f);
                        for (int s = -1; s <= 1; s += 2) Ball("Cup", headT, new Vector3(0.13f * s, 0f, 0f), 0.045f, a.Gear(GearSlot.Accessory).primary);
                    }
                    break;
            }
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private void BuildHair(Transform head, CharacterAppearance a)
        {
            string c = a.hairColor;
            switch (a.hairStyle)
            {
                case 1: Ball("Hair", head, new Vector3(0f, 0.015f, -0.005f), 0.127f, c, true, 0.2f); break;
                case 2: Ball("Hair", head, new Vector3(0f, 0.005f, 0f), 0.123f, c, true, 0.1f); break;
                case 3:
                    Ball("Hair", head, new Vector3(0f, 0.015f, -0.005f), 0.128f, c, true, 0.2f);
                    Box("HairBack", head, Vector3.zero, new Vector3(0.22f, 0.22f, 0.06f), c, 0.2f).localPosition = new Vector3(0f, -0.13f, -0.09f);
                    break;
                case 4:
                    Ball("Hair", head, new Vector3(0f, 0.015f, -0.005f), 0.127f, c, true, 0.2f);
                    Box("Ponytail", head, Vector3.zero, new Vector3(0.05f, 0.16f, 0.05f), c, 0.2f).localPosition = new Vector3(0f, -0.1f, -0.14f);
                    break;
                case 5: Box("Mohawk", head, Vector3.zero, new Vector3(0.035f, 0.07f, 0.22f), c, 0.2f).localPosition = new Vector3(0f, 0.18f, -0.01f); break;
            }
        }
    }
}
