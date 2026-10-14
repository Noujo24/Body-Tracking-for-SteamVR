using GTSteamVRBodyTracking.Core;
using UnityEngine;

namespace GTSteamVRBodyTracking.Extensions
{
    /// <summary>Zero-cost conversions between the Unity-free Core math types and UnityEngine types (same conventions).</summary>
    internal static class UnityConvertExtensions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static Vec3 ToCore(this Vector3 v) => new Vec3(v.x, v.y, v.z);
        public static Quat ToCore(this Quaternion q) => new Quat(q.x, q.y, q.z, q.w);
        public static RigidPose WorldPose(this Transform t) => new RigidPose(t.position.ToCore(), t.rotation.ToCore());
        public static RigidPose LocalPose(this Transform t) => new RigidPose(t.localPosition.ToCore(), t.localRotation.ToCore());
    }
}
