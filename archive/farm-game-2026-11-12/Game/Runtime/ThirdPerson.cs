using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// The visible player: our own suited character (OwnCharacter) on an animated rig (Kevin Iglesias' Human Basic Motions, built into
    /// Resources/Player/PlayerAvatar by the editor) and a camera over the shoulder that never goes through walls.
    /// The game starts in first person: your body is drawn without its head, set a little back, and HandsIK holds
    /// your hands up at the bottom of the view. V switches to a close over-the-shoulder view. The eye pivot stays at head height
    /// and carries the mouse pitch, so aiming, reach and held tools work exactly as before. If the avatar is
    /// missing, the game stays first-person. Presentation only.
    /// </summary>
    public sealed class ThirdPerson : MonoBehaviour
    {
        static readonly Vector3 Shoulder = new Vector3(0.45f, 0f, -0.95f);
        const float CamRadius = 0.25f;

        Transform _player, _eye, _cam;
        CharacterController _cc;
        Animator _anim;
        Transform _avatar;
        Renderer[] _renderers;
        bool _third = false;            // first person by default; V switches to over the shoulder
        Vector3 _lastPos;
        float _speed;
        float _camDist = 0.95f;
        Transform _head;
        HandsIK _hands;
        const float BodyBack = 0.22f;     // first person: the body sits this far behind the eye, so you don't see into your neck
        static readonly int SpeedId = Animator.StringToHash("Speed"), GroundedId = Animator.StringToHash("Grounded");

        public bool IsThirdPerson => _third && _anim != null;

        public void Init(Transform player, Transform eye, Transform cam, CharacterController cc)
        {
            _player = player; _eye = eye; _cam = cam; _cc = cc;
            _lastPos = player.position;
            var prefab = Resources.Load<GameObject>("Player/PlayerAvatar");
            if (prefab == null)
            {
                Debug.Log("No player avatar yet (Mars Needs Water > Rebuild Player Avatar needs the Human Basic Motions pack): first person only.");
                _third = false;
                Apply();
                return;
            }
            var go = Instantiate(prefab, player, false);
            go.name = "Avatar";
            _avatar = go.transform;
            _avatar.localPosition = Vector3.zero;
            _avatar.localRotation = Quaternion.identity;
            _anim = go.GetComponent<Animator>();
            if (_anim != null) _anim.applyRootMotion = false;
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            if (_anim != null)
            {
                _head = _anim.GetBoneTransform(HumanBodyBones.Head);
                // Size the body so its head joint sits just under the eye: otherwise the camera ends up inside
                // the shoulders and the first-person view is mostly orange suit.
                if (_head != null)
                {
                    float headY = _avatar.InverseTransformPoint(_head.position).y;
                    float eyeY = player.InverseTransformPoint(eye.position).y;
                    if (headY > 0.5f) _avatar.localScale = Vector3.one * Mathf.Clamp((eyeY - 0.08f) / headY, 0.7f, 1.3f);
                }
                _hands = go.AddComponent<HandsIK>();
                _hands.Eye = eye;
            }
            _renderers = go.GetComponentsInChildren<Renderer>();
            // Our own character rides on the rig's bones; the pack's body is then hidden (it still animates).
            var own = OwnCharacter.Build(_anim, _avatar);
            if (own != null)
            {
                foreach (var r in _renderers) r.enabled = false;
                _renderers = own;
            }
            else
            {
                // Fallback: the pack's body in a plain work suit (its demo material is not URP).
                var suit = Prims.Mat(new Color(0.95f, 0.55f, 0.22f), 0.25f, 0f);
                foreach (var r in _renderers)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = suit;
                    r.sharedMaterials = mats;
                }
            }
            Apply();
        }

        void Apply()
        {
            bool third = IsThirdPerson;
            if (_renderers != null)
                foreach (var r in _renderers) r.shadowCastingMode = ShadowCastingMode.On;
            // First person: no head (it would fill the view), hands up in front of you.
            if (_head != null) _head.localScale = third ? Vector3.one : Vector3.one * 0.001f;
            if (_hands != null) _hands.Target = third ? 0f : 1f;
            if (_avatar != null) _avatar.localPosition = third ? Vector3.zero : new Vector3(0f, 0f, -BodyBack);
            if (!third && _cam != null) _cam.localPosition = Vector3.zero;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame && _anim != null) { _third = !_third; Apply(); }
            if (_anim == null || _player == null) return;

            // Speed from how far the player really moved (works for walking, boosting, swimming and leaps).
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            var delta = _player.position - _lastPos;
            _lastPos = _player.position;
            var flat = new Vector3(delta.x, 0f, delta.z);
            if (flat.magnitude > 50f) flat = Vector3.zero;          // a re-base or a respawn, not movement
            float v = flat.magnitude / dt;
            _speed = Mathf.Lerp(_speed, v, 1f - Mathf.Exp(-10f * dt));
            _anim.SetFloat(SpeedId, Mathf.Min(_speed, 7f));
            _anim.speed = _speed > 7f ? Mathf.Min(1f + (_speed - 7f) / 25f, 3f) : 1f; // boost: legs go faster
            bool grounded = _cc == null || _cc.isGrounded || delta.y > -0.02f && delta.y < 0.02f;
            _anim.SetBool(GroundedId, grounded);

            // Third person: face the way you are moving (strafing and backing up turn the body), otherwise the camera's way.
            // First person: always face the camera's way, so the arms stay in front of you.
            if (!_third) _avatar.localRotation = Quaternion.Slerp(_avatar.localRotation, Quaternion.identity, 1f - Mathf.Exp(-12f * dt));
            else if (_speed > 0.4f && flat.sqrMagnitude > 1e-6f)
            {
                var local = _player.InverseTransformDirection(flat.normalized);
                local.y = 0f;
                if (local.sqrMagnitude > 1e-4f)
                {
                    var want = Quaternion.LookRotation(local.normalized, Vector3.up);
                    _avatar.localRotation = Quaternion.Slerp(_avatar.localRotation, want, 1f - Mathf.Exp(-12f * dt));
                }
            }
            else _avatar.localRotation = Quaternion.Slerp(_avatar.localRotation, Quaternion.identity, 1f - Mathf.Exp(-4f * dt));
        }

        void LateUpdate()
        {
            if (!IsThirdPerson || _cam == null || _eye == null) return;
            // Over the shoulder, pulled in when a wall, a tree or the ground is in the way.
            var want = _eye.TransformPoint(Shoulder);
            var from = _eye.position + _eye.right * Shoulder.x * 0.5f;
            var dir = want - from;
            float full = dir.magnitude;
            float dist = full;
            foreach (var hit in Physics.SphereCastAll(from, CamRadius, dir / full, full, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(_player)) continue;
                if (hit.distance < dist) dist = hit.distance;
            }
            // Snap in fast, ease back out.
            _camDist = dist < _camDist ? dist : Mathf.Lerp(_camDist, dist, 1f - Mathf.Exp(-5f * Time.deltaTime));
            _cam.position = from + dir / full * _camDist;
        }
    }
}
