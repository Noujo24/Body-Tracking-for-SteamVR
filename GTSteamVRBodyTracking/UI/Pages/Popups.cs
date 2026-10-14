using GTSteamVRBodyTracking.Behaviours;
using System;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    /// <summary>The body-role dropdown: a modal list of all roles, with conflict handling on selection.</summary>
    internal sealed class RolePickerPopup
    {
        private const float BoxX = 50f, BoxY = 120f, BoxW = 800f, BoxH = 880f;

        private readonly MenuController _menu;
        private readonly RectTransform _root;
        private readonly Text _title;
        private readonly UIButton[] _roleButtons = new UIButton[BodyRoles.All.Length];
        private readonly RectTransform _conflictGroup;
        private readonly Text _question;
        private TrackerRuntime _target;
        private BodyRole _pending;

        public bool IsOpen => _root.gameObject.activeSelf;

        public RolePickerPopup(MenuController menu, RectTransform canvas)
        {
            _menu = menu;
            _root = UIFactory.Rect(canvas, "RolePicker");
            UIFactory.Stretch(_root);
            UIFactory.AddImage(_root, new Color(0f, 0f, 0f, 0.65f));
            UIFactory.Blocker(_root);

            Image box = UIFactory.Panel(_root, "Box", BoxX, BoxY, BoxW, BoxH, UIStyle.Header);
            Transform b = box.transform;
            _title = UIFactory.Label(b, "", 24f, 14f, BoxW - 48f, 70f, UIStyle.FontLarge, TextAnchor.MiddleLeft);

            for (int i = 0; i < BodyRoles.All.Length; i++)
            {
                BodyRole role = BodyRoles.All[i];
                float x = 24f + (i % 2) * 382f;
                float y = 100f + (i / 2) * 76f;
                _roleButtons[i] = UIFactory.Button(b, BodyRoles.DisplayName(role), x, y, 370f, 66f, () => Pick(role), UIStyle.FontSmall);
            }
            UIFactory.Button(b, "Cancel", 24f, BoxH - 90f, BoxW - 48f, 66f, Hide);

            _conflictGroup = UIFactory.Container(b, "Conflict", 0f, 90f, BoxW, BoxH - 90f);
            UIFactory.AddImage(_conflictGroup, UIStyle.Header);
            UIFactory.Blocker(_conflictGroup);
            _question = UIFactory.Label(_conflictGroup, "", 24f, 10f, BoxW - 48f, 200f, UIStyle.FontNormal, TextAnchor.UpperLeft);
            UIFactory.Button(_conflictGroup, "Move here (unassign the other tracker)", 24f, 230f, BoxW - 48f, 70f, () => Commit(AssignMode.MoveHere));
            UIFactory.Button(_conflictGroup, "Swap roles", 24f, 315f, BoxW - 48f, 70f, () => Commit(AssignMode.Swap));
            UIFactory.Button(_conflictGroup, "Assign anyway (conflict - role not applied)", 24f, 400f, BoxW - 48f, 70f, () => Commit(AssignMode.AllowConflict));
            UIFactory.Button(_conflictGroup, "Back", 24f, 485f, BoxW - 48f, 70f, () => _conflictGroup.gameObject.SetActive(false));

            _conflictGroup.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
        }

        public void Show(TrackerRuntime target)
        {
            _target = target;
            _conflictGroup.gameObject.SetActive(false);
            _root.gameObject.SetActive(true);
            Refresh(Time.unscaledTime);
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
            _target = null;
        }

        public void Refresh(float now)
        {
            if (!IsOpen || _target == null) return;
            ModRuntime rt = _menu.Runtime;
            if (rt.Tracker.Find(_target.Key) == null)
            {
                Hide();
                _menu.Notify("That tracker is no longer known; role picker closed.", Severity.Warning);
                return;
            }
            UIFactory.SetText(_title, "Assign role: " + _target.Record.DisplayName + " " + TrackerIdentity.ShortId(_target.Record.Serial));
            for (int i = 0; i < BodyRoles.All.Length; i++)
            {
                BodyRole role = BodyRoles.All[i];
                TrackerSettings holder = AssignmentOps.FindOtherHolder(role, rt.Status.AllSettings, _target.Settings);
                string label = BodyRoles.DisplayName(role);
                if (holder != null) label += "  (used by " + TrackerIdentity.ShortId(holder.Serial) + ")";
                else if (BodyRoles.Support(role) == AvatarSupport.MarkerOnly && role != BodyRole.Unassigned) label += "  (marker only)";
                UIButton b = _roleButtons[i];
                b.SetLabel(label);
                b.NormalColor = role == _target.Role ? UIStyle.Accent : holder != null ? new Color(0.35f, 0.25f, 0.18f, 1f) : UIStyle.Button;
            }
        }

        private void Pick(BodyRole role)
        {
            if (_target == null) return;
            _pending = role;
            ModRuntime rt = _menu.Runtime;
            TrackerSettings holder = AssignmentOps.FindOtherHolder(role, rt.Status.AllSettings, _target.Settings);
            if (holder != null && role != _target.Role)
            {
                UIFactory.SetText(_question,
                    $"{BodyRoles.DisplayName(role)} is already assigned to tracker {TrackerIdentity.ShortId(holder.Serial)}.\n\n" +
                    $"Move here: that tracker becomes Unassigned.\nSwap: it takes this tracker's current role ({BodyRoles.DisplayName(_target.Role)}).");
                _conflictGroup.gameObject.SetActive(true);
                return;
            }
            Commit(AssignMode.MoveHere);
        }

        private void Commit(AssignMode mode)
        {
            if (_target == null) return;
            ModRuntime rt = _menu.Runtime;
            int others = AssignmentOps.Assign(_target.Settings, _pending, rt.Status.AllSettings, mode);
            rt.OnTrackerSettingsChanged(_target);

            string msg = $"{TrackerIdentity.ShortId(_target.Record.Serial)} is now {BodyRoles.DisplayName(_pending)}.";
            if (others > 0) msg += mode == AssignMode.Swap ? " Roles swapped." : " Other tracker unassigned.";
            AvatarSupport support = BodyRoles.Support(_pending);
            if (support == AvatarSupport.BodyRotation) msg += " Calibrate to drive the torso.";
            else if (support == AvatarSupport.MarkerOnly && _pending != BodyRole.Unassigned) msg += " Marker only (no gorilla bone).";
            if (!_target.SettingsPersistent) msg += " (session only: no stable serial)";
            Severity sev = mode == AssignMode.AllowConflict ? Severity.Warning : Severity.Info;
            if (mode == AssignMode.AllowConflict) msg += " CONFLICT: role not applied until resolved.";
            Hide();
            _menu.Notify(msg, sev);
        }
    }

    /// <summary>Generic yes/no confirmation for destructive actions.</summary>
    internal sealed class ConfirmPopup
    {
        private readonly RectTransform _root;
        private readonly Text _message;
        private readonly UIButton _confirm;
        private Action _onConfirm;

        public ConfirmPopup(MenuController menu, RectTransform canvas)
        {
            _root = UIFactory.Rect(canvas, "Confirm");
            UIFactory.Stretch(_root);
            UIFactory.AddImage(_root, new Color(0f, 0f, 0f, 0.7f));
            UIFactory.Blocker(_root);
            Image box = UIFactory.Panel(_root, "Box", 90f, 330f, 720f, 420f, UIStyle.Header);
            _message = UIFactory.Label(box.transform, "", 24f, 20f, 672f, 230f, UIStyle.FontNormal, TextAnchor.UpperLeft);
            _confirm = UIFactory.Button(box.transform, "Confirm", 24f, 300f, 330f, 80f, () =>
            {
                Action a = _onConfirm;
                Hide();
                a?.Invoke();
            });
            _confirm.NormalColor = new Color(0.55f, 0.22f, 0.2f, 1f);
            UIFactory.Button(box.transform, "Cancel", 366f, 300f, 330f, 80f, Hide);
            _root.gameObject.SetActive(false);
        }

        public void Show(string message, string confirmLabel, Action onConfirm)
        {
            _onConfirm = onConfirm;
            UIFactory.SetText(_message, message);
            _confirm.SetLabel(confirmLabel);
            _root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
            _onConfirm = null;
        }
    }
}
