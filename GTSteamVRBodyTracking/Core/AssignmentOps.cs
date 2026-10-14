using System.Collections.Generic;

namespace GTSteamVRBodyTracking.Core
{
    public enum AssignMode
    {
        /// <summary>Assign even if another tracker holds the role (a conflict warning is then shown).</summary>
        AllowConflict,
        /// <summary>Unassign every other tracker holding the role.</summary>
        MoveHere,
        /// <summary>Give the other holder this tracker's previous role.</summary>
        Swap
    }

    /// <summary>Role assignment operations. Changes apply immediately; no restart required.</summary>
    public static class AssignmentOps
    {
        /// <summary>Returns the number of OTHER trackers whose role changed.</summary>
        public static int Assign(TrackerSettings target, BodyRole role, IReadOnlyList<TrackerSettings> all, AssignMode mode)
        {
            BodyRole previous = target.Role;
            int changed = 0;
            if (BodyRoles.IsUnique(role) && mode != AssignMode.AllowConflict)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    TrackerSettings other = all[i];
                    if (other == null || ReferenceEquals(other, target) || other.Role != role) continue;
                    other.Role = mode == AssignMode.Swap ? previous : BodyRole.Unassigned;
                    changed++;
                }
            }
            target.Role = role;
            return changed;
        }

        /// <summary>Conflict resolution: keep <paramref name="keep"/> on the role and unassign everyone else holding it.</summary>
        public static int KeepOnly(TrackerSettings keep, BodyRole role, IReadOnlyList<TrackerSettings> all)
        {
            int changed = 0;
            for (int i = 0; i < all.Count; i++)
            {
                TrackerSettings s = all[i];
                if (s == null || ReferenceEquals(s, keep) || s.Role != role) continue;
                s.Role = BodyRole.Unassigned;
                changed++;
            }
            if (keep != null) keep.Role = role;
            return changed;
        }

        public static int UnassignAll(BodyRole role, IReadOnlyList<TrackerSettings> all)
        {
            int changed = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || all[i].Role != role) continue;
                all[i].Role = BodyRole.Unassigned;
                changed++;
            }
            return changed;
        }

        /// <summary>Another enabled tracker currently holding a unique role, or null.</summary>
        public static TrackerSettings FindOtherHolder(BodyRole role, IReadOnlyList<TrackerSettings> all, TrackerSettings except)
        {
            if (!BodyRoles.IsUnique(role)) return null;
            for (int i = 0; i < all.Count; i++)
            {
                TrackerSettings s = all[i];
                if (s != null && !ReferenceEquals(s, except) && s.Enabled && s.Role == role) return s;
            }
            return null;
        }
    }
}
