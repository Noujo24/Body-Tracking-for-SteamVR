using System;
using System.Collections.Generic;

namespace GTSteamVRBodyTracking.Core
{
    public enum BodyRole
    {
        Unassigned = 0,
        Waist = 1,
        Chest = 2,
        LeftFoot = 3,
        RightFoot = 4,
        LeftKnee = 5,
        RightKnee = 6,
        LeftElbow = 7,
        RightElbow = 8,
        LeftHip = 9,
        RightHip = 10,
        Custom = 11
    }

    /// <summary>What the verified Gorilla Tag rig can do with a role (Docs/GAME_INTEGRATION.md §5.5).</summary>
    public enum AvatarSupport
    {
        /// <summary>No bone exists on the gorilla rig; debug marker only.</summary>
        MarkerOnly,
        /// <summary>Drives GorillaIK.bodyBone rotation (via targetBodyRot).</summary>
        BodyRotation,
        /// <summary>Drives GorillaIK.left/rightElbowDirection (elbow pole / arm twist).</summary>
        ElbowDirection
    }

    public static class BodyRoles
    {
        /// <summary>Order used by the assignment dropdown.</summary>
        public static readonly BodyRole[] All =
        {
            BodyRole.Unassigned, BodyRole.Waist, BodyRole.Chest,
            BodyRole.LeftFoot, BodyRole.RightFoot, BodyRole.LeftKnee, BodyRole.RightKnee,
            BodyRole.LeftElbow, BodyRole.RightElbow, BodyRole.LeftHip, BodyRole.RightHip,
            BodyRole.Custom
        };

        public static string DisplayName(BodyRole role)
        {
            switch (role)
            {
                case BodyRole.Unassigned: return "Unassigned";
                case BodyRole.Waist: return "Waist/pelvis";
                case BodyRole.Chest: return "Chest";
                case BodyRole.LeftFoot: return "Left foot";
                case BodyRole.RightFoot: return "Right foot";
                case BodyRole.LeftKnee: return "Left knee";
                case BodyRole.RightKnee: return "Right knee";
                case BodyRole.LeftElbow: return "Left elbow";
                case BodyRole.RightElbow: return "Right elbow";
                case BodyRole.LeftHip: return "Left hip";
                case BodyRole.RightHip: return "Right hip";
                case BodyRole.Custom: return "Custom";
                default: return role.ToString();
            }
        }

        /// <summary>Every real body location may be driven by at most one tracker; Unassigned/Custom may repeat.</summary>
        public static bool IsUnique(BodyRole role) => role != BodyRole.Unassigned && role != BodyRole.Custom;

        public static AvatarSupport Support(BodyRole role)
        {
            switch (role)
            {
                case BodyRole.Waist:
                case BodyRole.Chest:
                    return AvatarSupport.BodyRotation;
                case BodyRole.LeftElbow:
                case BodyRole.RightElbow:
                    return AvatarSupport.ElbowDirection;
                default:
                    return AvatarSupport.MarkerOnly;
            }
        }

        public static string SupportDescription(BodyRole role)
        {
            switch (Support(role))
            {
                case AvatarSupport.BodyRotation: return "Avatar: torso rotation";
                case AvatarSupport.ElbowDirection: return "Avatar: elbow direction";
                default:
                    return role == BodyRole.Unassigned
                        ? "Not used"
                        : "Marker only (gorilla rig has no " + DisplayName(role).ToLowerInvariant() + " bone)";
            }
        }

        public static bool TryParse(string text, out BodyRole role)
        {
            role = BodyRole.Unassigned;
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!Enum.TryParse(text.Trim(), true, out BodyRole parsed)) return false;
            if (!Enum.IsDefined(typeof(BodyRole), parsed)) return false;
            role = parsed;
            return true;
        }
    }

    public struct RoleClaim
    {
        public string TrackerKey;
        public BodyRole Role;
        public bool Enabled;

        public RoleClaim(string trackerKey, BodyRole role, bool enabled)
        {
            TrackerKey = trackerKey;
            Role = role;
            Enabled = enabled;
        }
    }

    /// <summary>Detects unique roles claimed by more than one enabled tracker.</summary>
    public sealed class RoleConflictDetector
    {
        private readonly Dictionary<BodyRole, List<string>> _claims = new Dictionary<BodyRole, List<string>>();
        private readonly List<BodyRole> _conflicted = new List<BodyRole>();

        /// <summary>Roles currently claimed by two or more enabled trackers.</summary>
        public IReadOnlyList<BodyRole> ConflictedRoles => _conflicted;

        public void Evaluate(IReadOnlyList<RoleClaim> claims)
        {
            foreach (List<string> list in _claims.Values) list.Clear();
            _conflicted.Clear();

            for (int i = 0; i < claims.Count; i++)
            {
                RoleClaim c = claims[i];
                if (!c.Enabled || !BodyRoles.IsUnique(c.Role)) continue;
                if (!_claims.TryGetValue(c.Role, out List<string> list))
                {
                    list = new List<string>();
                    _claims[c.Role] = list;
                }
                list.Add(c.TrackerKey);
            }

            foreach (BodyRole role in BodyRoles.All)
                if (_claims.TryGetValue(role, out List<string> list) && list.Count > 1)
                    _conflicted.Add(role);
        }

        public bool IsConflicted(BodyRole role) => _conflicted.Contains(role);

        public IReadOnlyList<string> KeysFor(BodyRole role) =>
            _claims.TryGetValue(role, out List<string> list) ? (IReadOnlyList<string>)list : Array.Empty<string>();
    }
}
