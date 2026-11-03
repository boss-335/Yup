using UnityEngine;

namespace VoiceHorror.Characters
{
    /// <summary>
    /// Analytic two-bone IK for limbs whose bones point along local -Y (the blueprint convention).
    /// The middle joint bends toward <c>pole</c>; <c>forwardHint</c> keeps the limb's twist stable.
    /// </summary>
    public static class TwoBoneIK
    {
        public static void Solve(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, Vector3 forwardHint)
        {
            Vector3 a = upper.position;
            float l1 = Vector3.Distance(a, lower.position);
            float l2 = Vector3.Distance(lower.position, end.position);
            if (l1 < 1e-4f || l2 < 1e-4f) return;

            Vector3 toTarget = target - a;
            float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.down;

            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(forwardHint, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.Cross(dir, Vector3.right);
            bend.Normalize();

            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 knee = a + dir * (l1 * cosA) + bend * (l1 * sinA);
            Vector3 endPos = a + dir * dist;

            upper.rotation = AimDown(knee - a, forwardHint);
            lower.rotation = AimDown(endPos - knee, forwardHint);
        }

        /// <summary>Rotation whose local -Y points along <paramref name="down"/>, local +Z near <paramref name="forwardHint"/>.</summary>
        public static Quaternion AimDown(Vector3 down, Vector3 forwardHint)
        {
            Vector3 up = -down.normalized;
            Vector3 f = Vector3.ProjectOnPlane(forwardHint, up);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(Vector3.right, up);
            return Quaternion.LookRotation(f.normalized, up);
        }
    }
}
