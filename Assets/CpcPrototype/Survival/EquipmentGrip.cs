using UnityEngine;

namespace CpcPrototype
{
    public sealed class EquipmentGrip : MonoBehaviour
    {
        public Animator Body;
        public Transform Equipment;
        public Transform PrimaryMarker;
        public Transform SecondaryMarker;
        public bool IsShield;
        public Transform RightPalm;
        public Transform LeftPalm;

        private Transform _primaryHand, _leftHand, _elbow, _shoulder, _rightElbow, _rightShoulder;


        private void Awake()
        {
            _primaryHand = Body.GetBoneTransform(HumanBodyBones.RightHand);
            _leftHand = Body.GetBoneTransform(HumanBodyBones.LeftHand);
            _elbow = Body.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _shoulder = Body.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rightElbow = Body.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightShoulder = Body.GetBoneTransform(HumanBodyBones.RightUpperArm);

        }

        // Place the firing hand within its physical reach before aligning the support hand.
        public void AlignAt(Vector3 worldGrip)
        {
            Vector3 palmOffset = RightPalm != null ? RightPalm.position - _primaryHand.position : Vector3.zero;
            SolveArm(_rightShoulder, _rightElbow, _primaryHand, worldGrip - palmOffset);
            Align();
        }

        public void Align()
        {
            Transform anchor = RightPalm != null ? RightPalm : _primaryHand;
            Equipment.position += anchor.position - PrimaryMarker.position;
            if (!IsShield) PlaceSupportHand();
        }

        private void PlaceSupportHand()
        {
            // Find a shared grip position both arms can reach while preserving the aim rotation.
            Vector3 rightOffset = RightPalm != null ? RightPalm.position - _primaryHand.position : Vector3.zero;
            Vector3 leftOffset = LeftPalm != null ? LeftPalm.position - _leftHand.position : Vector3.zero;
            Vector3 offset = SecondaryMarker.position - PrimaryMarker.position + rightOffset - leftOffset;
            Vector3 grip = _primaryHand.position;
            float leftReach = ArmLength(_shoulder, _elbow, _leftHand);
            float rightReach = ArmLength(_rightShoulder, _rightElbow, _primaryHand);
            for (int i = 0; i < 8; i++)
            {
                grip = _shoulder.position - offset + Vector3.ClampMagnitude(grip + offset - _shoulder.position, leftReach);
                grip = _rightShoulder.position + Vector3.ClampMagnitude(grip - _rightShoulder.position, rightReach);
            }
            SolveArm(_rightShoulder, _rightElbow, _primaryHand, grip);
            Equipment.position += (RightPalm != null ? RightPalm.position : _primaryHand.position) - PrimaryMarker.position;
            SolveArm(_shoulder, _elbow, _leftHand, SecondaryMarker.position - leftOffset);
        }

        private static float ArmLength(Transform shoulder, Transform elbow, Transform hand) =>
            Mathf.Max(0f, Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, hand.position) - .0002f);

        private void SolveArm(Transform shoulder, Transform joint, Transform hand, Vector3 target)
        {
            Vector3 start = shoulder.position;
            Vector3 upper = joint.position - start;
            Vector3 lower = hand.position - joint.position;
            Vector3 reach = target - start;
            float a = upper.magnitude;
            float b = lower.magnitude;
            if (reach.sqrMagnitude < 1e-8f || Mathf.Min(a, b) < .0001f) return;
            Quaternion wristRotation = hand.rotation;
            Vector3 axis = reach.normalized;
            float distance = Mathf.Clamp(reach.magnitude, Mathf.Abs(a - b) + .0001f, a + b - .0001f);
            Vector3 bend = Vector3.ProjectOnPlane(upper, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(-Body.transform.up, axis);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Body.transform.right, axis);
            float along = (a * a - b * b + distance * distance) / (2f * distance);
            Vector3 elbow = start + axis * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            shoulder.rotation = Quaternion.FromToRotation(upper, elbow - start) * shoulder.rotation;
            joint.rotation = Quaternion.FromToRotation(hand.position - joint.position,
                start + axis * distance - joint.position) * joint.rotation;
            hand.rotation = wristRotation;
        }
    }
}





