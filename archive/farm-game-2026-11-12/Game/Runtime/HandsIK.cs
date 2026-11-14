using UnityEngine;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// First-person arms: while Weight is up, the avatar's hands are pulled (Animator IK) to a ready pose at the
    /// bottom of the view, in front of the eye, so you see your own hands. When a tool is in hand
    /// (<see cref="Grip"/>), the right hand instead closes into a fist round the tool's handle, wherever the tool
    /// is drawn. Lives on the avatar, next to its Animator (the controller's base layer needs "IK Pass", which
    /// PlayerAvatarSetup turns on).
    /// </summary>
    public sealed class HandsIK : MonoBehaviour
    {
        // Hand spots in the eye's space (x right, y up, z forward): low in the frame, a little apart.
        public static readonly Vector3 RightSpot = new Vector3(0.2f, -0.2f, 0.34f);
        public static readonly Vector3 LeftSpot = new Vector3(-0.19f, -0.21f, 0.33f);
        static readonly Vector3 Elbow = new Vector3(0.4f, -0.55f, 0f);

        /// <summary>The handle of the tool in hand (z along the handle, towards the tool's working end), or null.</summary>
        public static Transform Grip;

        // In the fist model (own-char-fist): the point the handle passes through and the direction it runs.
        static readonly Vector3 FistHole = new Vector3(0.017f, 0.096f, 0f);
        static readonly Vector3 FistAxis = new Vector3(0f, 0.342f, 0.94f);

        public Transform Eye;
        public float Target;            // 1 in first person, 0 in third
        float _w;
        Animator _anim;
        Transform _wrist, _glove, _fist;
        bool _looked;

        void Awake() { _anim = GetComponent<Animator>(); }

        void Update() { _w = Mathf.MoveTowards(_w, Target, Time.deltaTime * 4f); }

        void FindHand()
        {
            _looked = true;
            _wrist = _anim.isHuman ? _anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (_wrist == null) return;
            _glove = _wrist.Find("own-char-glove");
            _fist = _wrist.Find("own-char-fist");
        }

        void OnAnimatorIK(int layer)
        {
            if (_anim == null || Eye == null) return;
            if (!_looked) FindHand();
            var r = Eye.rotation;
            bool grip = Grip != null && Grip.gameObject.activeInHierarchy && _fist != null && _glove != null && _w > 0.5f;
            if (_fist != null && _fist.gameObject.activeSelf != grip)
            {
                _fist.gameObject.SetActive(grip);
                _glove.gameObject.SetActive(!grip);
            }
            // Humanoid hand goals: fingers point along the arm in the T-pose; turn them to point forward, palms
            // tipped in towards each other a little, wrists dipped.
            if (grip) Hold();
            else Set(AvatarIKGoal.RightHand, AvatarIKHint.RightElbow, RightSpot, r * Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(-35f, 0f, 10f), 1f);
            Set(AvatarIKGoal.LeftHand, AvatarIKHint.LeftElbow, LeftSpot, r * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(35f, 0f, -10f), -1f);
        }

        /// <summary>
        /// Puts the fist round the handle: the fist's hole on the grip point, its hole axis along the handle, and
        /// the rest of the hand turned so the forearm bends as little as possible at the wrist.
        /// </summary>
        void Hold()
        {
            var goal = AvatarIKGoal.RightHand;
            Vector3 g = Grip.position, f = Grip.forward;
            var elbow = Eye.TransformPoint(Elbow);
            // The fist in the world: its hole axis along the handle, its arm axis as close to the forearm as it can be.
            var arm = (g - elbow).normalized;
            var armAcross = arm - f * Vector3.Dot(arm, f);
            if (armAcross.sqrMagnitude < 1e-5f) armAcross = Eye.up;
            var axis = FistAxis.normalized;
            var localAcross = Vector3.up - axis * Vector3.Dot(Vector3.up, axis);
            var fistRot = Quaternion.LookRotation(f, armAcross.normalized) * Quaternion.Inverse(Quaternion.LookRotation(axis, localAcross.normalized));
            float k = _fist.lossyScale.x;
            // The wrist bone that puts the fist there (the fist is a child of the wrist, at the wrist).
            var boneRot = fistRot * Quaternion.Inverse(_fist.localRotation);
            var bonePos = g - fistRot * (FistHole * k);
            // The rig's IK goal is offset from the wrist bone; read that offset from the current pose and keep it.
            var inv = Quaternion.Inverse(_wrist.rotation);
            var offRot = inv * _anim.GetIKRotation(goal);
            var offPos = inv * (_anim.GetIKPosition(goal) - _wrist.position);
            _anim.SetIKPositionWeight(goal, _w);
            _anim.SetIKRotationWeight(goal, _w);
            _anim.SetIKPosition(goal, bonePos + boneRot * offPos);
            _anim.SetIKRotation(goal, boneRot * offRot);
            _anim.SetIKHintPositionWeight(AvatarIKHint.RightElbow, _w * 0.6f);
            _anim.SetIKHintPosition(AvatarIKHint.RightElbow, elbow);
        }

        void Set(AvatarIKGoal goal, AvatarIKHint hint, Vector3 spot, Quaternion rot, float side)
        {
            _anim.SetIKPositionWeight(goal, _w);
            _anim.SetIKRotationWeight(goal, _w * 0.8f);
            _anim.SetIKPosition(goal, Eye.TransformPoint(spot));
            _anim.SetIKRotation(goal, rot);
            _anim.SetIKHintPositionWeight(hint, _w * 0.6f);
            _anim.SetIKHintPosition(hint, Eye.TransformPoint(new Vector3(0.4f * side, -0.55f, 0f)));
        }
    }
}
