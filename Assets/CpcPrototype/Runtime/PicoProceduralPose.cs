using UnityEngine;

namespace CpcPrototype
{
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class PicoProceduralPose : MonoBehaviour
    {
        private CharacterMotor _motor;
        private Transform _cachedTransform;
        private Transform _leftArm;
        private Transform _leftElbow;
        private Transform _rightArm;
        private Transform _rightElbow;
        private Transform _leftLeg;
        private Transform _leftKnee;
        private Transform _rightLeg;
        private Transform _rightKnee;
        private Quaternion _leftArmRest;
        private Quaternion _rightArmRest;
        private Quaternion _leftLegRest;
        private Quaternion _rightLegRest;

        private void Awake()
        {
            _motor = GetComponent<CharacterMotor>(); _cachedTransform = transform;
            Transform[] bones = GetComponentsInChildren<Transform>();
            for (int i = 0; i < bones.Length; i++)
            {
                switch (bones[i].name)
                {
                    case "LeftArm": _leftArm = bones[i]; break;
                    case "LeftForeArm": _leftElbow = bones[i]; break;
                    case "RightArm": _rightArm = bones[i]; break;
                    case "RightForeArm": _rightElbow = bones[i]; break;
                    case "LeftUpLeg": _leftLeg = bones[i]; break;
                    case "LeftLeg": _leftKnee = bones[i]; break;
                    case "RightUpLeg": _rightLeg = bones[i]; break;
                    case "RightLeg": _rightKnee = bones[i]; break;
                }
            }
            if (_leftArm == null || _rightArm == null || _leftLeg == null || _rightLeg == null) { enabled = false; return; }
            _leftArmRest = _leftArm.localRotation; _rightArmRest = _rightArm.localRotation;
            _leftLegRest = _leftLeg.localRotation; _rightLegRest = _rightLeg.localRotation;
        }

        private void LateUpdate()
        {
            // 클립 없는 테스트 리그용 절차적 포즈. 실제 Animator를 도입할 때 이 컴포넌트를 끈다.
            _leftArm.localRotation = _leftArmRest; _rightArm.localRotation = _rightArmRest;
            _leftLeg.localRotation = _leftLegRest; _rightLeg.localRotation = _rightLegRest;
            Vector3 forward = _cachedTransform.forward;
            Vector3 right = _cachedTransform.right;
            float swing = _motor.IsGrounded ? Mathf.Sin(Time.time * 10f) * Mathf.Clamp01(_motor.MotionSpeed / 3.5f) * 0.45f : 0f;
            PointBone(_leftArm, _leftElbow, -Vector3.up - right * 0.2f + forward * swing);
            PointBone(_rightArm, _rightElbow, _motor.IsAttacking ? forward + Vector3.up * 0.15f : -Vector3.up + right * 0.2f - forward * swing);
            PointBone(_leftLeg, _leftKnee, -Vector3.up - forward * swing);
            PointBone(_rightLeg, _rightKnee, -Vector3.up + forward * swing);
        }
        private static void PointBone(Transform bone, Transform child, Vector3 direction)
        {
            if (child == null) return;
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;
        }
    }
}
