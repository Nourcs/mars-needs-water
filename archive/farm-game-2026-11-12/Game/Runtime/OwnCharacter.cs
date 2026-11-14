using System.Collections.Generic;
using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Our own character: a suited intern (helmet, water pack, gloves, boots) made of rigid parts that ride on the
    /// bones of the animated humanoid, so every animation of the rig still works. The parts are the own-char-*
    /// models; limbs are stretched to each bone's length, so the suit fits whatever the rig's proportions are.
    /// Must be built while the rig is still in its rest pose (right after it is instantiated).
    /// </summary>
    public static class OwnCharacter
    {
        /// <summary>Builds the suit and returns its renderers, or null if the rig or the model files don't allow it.</summary>
        public static Renderer[] Build(Animator anim, Transform avatar)
        {
            if (anim == null || !anim.isHuman || Kit.Get("char-helmet") == null) return null;
            Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
            var hips = B(HumanBodyBones.Hips); var spine = B(HumanBodyBones.Spine); var head = B(HumanBodyBones.Head);
            var chest = B(HumanBodyBones.Chest); var neck = B(HumanBodyBones.Neck);
            if (hips == null || spine == null || head == null) return null;
            float k = avatar.lossyScale.y;
            var rot = avatar.rotation;
            var up = avatar.up;
            var list = new List<Renderer>();

            Place(list, "own-char-pelvis", hips, hips.position, rot, Vector3.one * k);
            var top = neck != null ? neck.position : Vector3.Lerp(spine.position, head.position, 0.85f);
            Place(list, "own-char-chest", chest != null ? chest : spine, spine.position, rot, new Vector3(k, Mathf.Max(0.05f, Vector3.Dot(top - spine.position, up)), k));
            Place(list, "own-char-helmet", head, head.position, rot, Vector3.one * k);

            for (int side = 0; side < 2; side++)
            {
                bool l = side == 0;
                var sh = B(l ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var el = B(l ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var wr = B(l ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var hp = B(l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var kn = B(l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                var an = B(l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (sh == null || el == null || wr == null || hp == null || kn == null || an == null) return Fail(list);
                Limb(list, "own-char-upperarm", sh, el, 0.082f, k);
                Limb(list, "own-char-forearm", el, wr, 0.068f, k);
                Limb(list, "own-char-thigh", hp, kn, 0.104f, k);
                Limb(list, "own-char-shin", kn, an, 0.088f, k);
                // Glove: its y runs on along the forearm, its flat side (x) is the back of the hand.
                var arm = (wr.position - el.position).normalized;
                var fwd = avatar.forward - arm * Vector3.Dot(avatar.forward, arm);
                if (fwd.sqrMagnitude < 1e-4f) fwd = up - arm * Vector3.Dot(up, arm);   // arms hanging straight forward: unlikely
                Place(list, "own-char-glove", wr, wr.position, Quaternion.LookRotation(fwd.normalized, arm), Vector3.one * k);
                if (!l)
                {
                    // A closed fist in the same place, hidden until the hand holds a tool (HandsIK swaps the two).
                    int before = list.Count;
                    Place(list, "own-char-fist", wr, wr.position, Quaternion.LookRotation(fwd.normalized, arm), Vector3.one * k);
                    if (list.Count > before) list[list.Count - 1].gameObject.SetActive(false);
                }
                // Boot: the model's ankle is 0.09 above its sole.
                float ankle = Mathf.Max(0.03f, Vector3.Dot(an.position - avatar.position, up));
                Place(list, "own-char-boot", an, an.position, rot, new Vector3(k, ankle / 0.09f, k));
            }
            return list.ToArray();
        }

        static Renderer[] Fail(List<Renderer> made)
        {
            foreach (var r in made) Object.Destroy(r.gameObject);
            return null;
        }

        static void Limb(List<Renderer> list, string model, Transform a, Transform b, float jointRadius, float k)
        {
            var d = b.position - a.position;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Place(list, model, a, a.position, Quaternion.FromToRotation(Vector3.up, d / len), new Vector3(k, len, k));
            Place(list, "own-char-joint", a, a.position, Quaternion.identity, Vector3.one * (jointRadius * k));
        }

        /// <summary>A part under a bone, at a world position, rotation and size (bones may carry their own scale).</summary>
        static void Place(List<Renderer> list, string model, Transform bone, Vector3 pos, Quaternion rot, Vector3 size)
        {
            var g = Own.Make(model, model, bone, Vector3.zero, 0.4f);
            if (g == null) return;
            var t = g.transform;
            t.position = pos;
            t.rotation = rot;
            var ps = bone.lossyScale;
            t.localScale = new Vector3(size.x / Mathf.Max(1e-5f, ps.x), size.y / Mathf.Max(1e-5f, ps.y), size.z / Mathf.Max(1e-5f, ps.z));
            var r = g.GetComponent<Renderer>();
            if (r != null) list.Add(r);
        }
    }
}
