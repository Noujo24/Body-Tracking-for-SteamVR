using System.Collections.Generic;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    /// <summary>Lists every unique role claimed by several enabled trackers and resolves them with one press.</summary>
    internal sealed class ConflictsPage : MenuPage
    {
        private const int MaxRows = 5;
        private const int KeepButtons = 3;

        private sealed class Row
        {
            public RectTransform Root;
            public Text Label;
            public UIButton[] Keep = new UIButton[KeepButtons];
            public string[] KeepKeys = new string[KeepButtons];
            public UIButton UnassignAll;
            public BodyRole Role;
        }

        private readonly Row[] _rows = new Row[MaxRows];
        private readonly Text _empty;

        public override string Title => "Role conflicts";

        public ConflictsPage(MenuController menu) : base(menu, "Conflicts")
        {
            UIFactory.Label(Root, "Each body location may be driven by one tracker. Conflicted roles are not applied until resolved. Keep one tracker or unassign all.",
                0f, 0f, MenuController.ContentWidth, 70f, UIStyle.FontSmall, TextAnchor.UpperLeft, UIStyle.TextDim);
            for (int i = 0; i < MaxRows; i++)
            {
                var row = new Row();
                row.Root = UIFactory.Container(Root, "Row", 0f, 80f + i * 140f, MenuController.ContentWidth, 132f);
                UIFactory.AddImage(row.Root, UIStyle.Card);
                row.Label = UIFactory.Label(row.Root, "", 14f, 6f, 840f, 44f, UIStyle.FontNormal);
                for (int k = 0; k < KeepButtons; k++)
                {
                    int slot = k;
                    Row r = row;
                    row.Keep[k] = UIFactory.Button(row.Root, "Keep", 14f + k * 214f, 56f, 204f, 64f, () => Keep(r, slot), UIStyle.FontSmall);
                }
                row.UnassignAll = UIFactory.Button(row.Root, "Unassign all", 14f + KeepButtons * 214f, 56f, 210f, 64f, () => UnassignAll(row), UIStyle.FontSmall);
                _rows[i] = row;
            }
            _empty = UIFactory.Label(Root, "No role conflicts.", 0f, 120f, MenuController.ContentWidth, 60f, UIStyle.FontLarge, TextAnchor.MiddleCenter, UIStyle.Good);
            UIFactory.Button(Root, "< Back", 0f, MenuController.ContentHeight - 70f, 280f, 66f, () => Menu.Show(Menu.Dashboard));
        }

        private void Keep(Row row, int slot)
        {
            TrackerRuntime keep = Rt.Tracker.Find(row.KeepKeys[slot]);
            if (keep == null) return;
            int changed = AssignmentOps.KeepOnly(keep.Settings, row.Role, Rt.Status.AllSettings);
            Rt.OnTrackerSettingsChanged(keep);
            Menu.Notify($"{BodyRoles.DisplayName(row.Role)} kept on {TrackerIdentity.ShortId(keep.Record.Serial)}; {changed} other tracker(s) unassigned.");
        }

        private void UnassignAll(Row row)
        {
            int changed = AssignmentOps.UnassignAll(row.Role, Rt.Status.AllSettings);
            Rt.OnTrackerSettingsChanged(null);
            Menu.Notify($"{BodyRoles.DisplayName(row.Role)} removed from {changed} tracker(s).");
        }

        public override void Refresh(float now)
        {
            IReadOnlyList<BodyRole> roles = Rt.Status.Conflicts.ConflictedRoles;
            UIFactory.SetActive(_empty, roles.Count == 0);
            for (int i = 0; i < MaxRows; i++)
            {
                Row row = _rows[i];
                bool show = i < roles.Count;
                UIFactory.SetActive(row.Root, show);
                if (!show) continue;

                row.Role = roles[i];
                IReadOnlyList<string> keys = Rt.Status.Conflicts.KeysFor(row.Role);
                UIFactory.SetText(row.Label, $"{BodyRoles.DisplayName(row.Role)}: {keys.Count} trackers" + (keys.Count > KeepButtons ? $" (showing {KeepButtons}; use Unassign all for the rest)" : ""));
                for (int k = 0; k < KeepButtons; k++)
                {
                    bool has = k < keys.Count;
                    UIFactory.SetActive(row.Keep[k].Rect, has);
                    row.KeepKeys[k] = has ? keys[k] : null;
                    if (!has) continue;
                    TrackerRuntime t = Rt.Tracker.Find(keys[k]);
                    string state = t == null ? "?" : t.Connected ? "" : " (off)";
                    row.Keep[k].SetLabel("Keep " + TrackerIdentity.ShortId(t != null ? t.Record.Serial : keys[k]) + state);
                }
            }
        }
    }
}
