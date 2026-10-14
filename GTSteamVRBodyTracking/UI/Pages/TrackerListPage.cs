using GTSteamVRBodyTracking.Behaviours;
using System.Collections.Generic;
using GTSteamVRBodyTracking.Core;
using GTSteamVRBodyTracking.Status;
using GTSteamVRBodyTracking.Tracking;
using UnityEngine;
using UnityEngine.UI;

namespace GTSteamVRBodyTracking.UI.Pages
{
    /// <summary>
    /// One card per known tracker, generated from the live tracker list. Cards are pooled per page and
    /// re-bound by key on every refresh, so connects/disconnects never leave stale or duplicate cards.
    /// There is no maximum tracker count: additional trackers go onto further pages.
    /// </summary>
    internal sealed class TrackerListPage : MenuPage
    {
        private const int CardsPerPage = 4;
        private const float CardHeight = 192f;
        private const float CardGap = 10f;
        private const float ListTop = 56f;

        private readonly TrackerCard[] _cards = new TrackerCard[CardsPerPage];
        private readonly Text _summary;
        private readonly Text _empty;
        private readonly Text _pageLabel;
        private readonly UIButton _prev;
        private readonly UIButton _next;
        private int _page;

        public override string Title => "Trackers";

        public TrackerListPage(MenuController menu) : base(menu, "TrackerList")
        {
            _summary = UIFactory.Label(Root, "", 0f, 0f, 640f, 48f, UIStyle.FontNormal, TextAnchor.MiddleLeft, UIStyle.TextDim);
            UIFactory.Button(Root, "Refresh", MenuController.ContentWidth - 220f, 0f, 220f, 48f, () =>
            {
                Rt.Tracker.RequestRefresh();
                Menu.Notify("Rescanning SteamVR devices...");
            });

            for (int i = 0; i < CardsPerPage; i++)
                _cards[i] = new TrackerCard(this, Root, ListTop + i * (CardHeight + CardGap), CardHeight);

            _empty = UIFactory.Label(Root, "No Vive Trackers detected yet.\n\nTurn the trackers on, make sure SteamVR shows them as tracked (solid icons), then press Refresh. Headsets, controllers and base stations are not listed.",
                40f, ListTop + 40f, MenuController.ContentWidth - 80f, 300f, UIStyle.FontNormal, TextAnchor.UpperCenter, UIStyle.TextDim);

            float by = ListTop + CardsPerPage * (CardHeight + CardGap) + 4f;
            _prev = UIFactory.Button(Root, "< Prev", 0f, by, 200f, 56f, () => { _page--; Refresh(Time.unscaledTime); });
            _pageLabel = UIFactory.Label(Root, "", 210f, by, MenuController.ContentWidth - 420f, 56f, UIStyle.FontNormal, TextAnchor.MiddleCenter, UIStyle.TextDim);
            _next = UIFactory.Button(Root, "Next >", MenuController.ContentWidth - 200f, by, 200f, 56f, () => { _page++; Refresh(Time.unscaledTime); });
        }

        public void ShowTracker(string key)
        {
            IReadOnlyList<TrackerRuntime> list = Rt.Tracker.Trackers;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Key == key) _page = i / CardsPerPage;
        }

        public override void Refresh(float now)
        {
            IReadOnlyList<TrackerRuntime> list = Rt.Tracker.Trackers;
            int pages = Mathf.Max(1, (list.Count + CardsPerPage - 1) / CardsPerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);

            ModStatus s = Rt.Status;
            UIFactory.SetText(_summary, $"{s.ConnectedCount} connected, {s.ValidCount} tracking, {list.Count} known");
            UIFactory.SetActive(_empty, list.Count == 0);
            UIFactory.SetText(_pageLabel, list.Count == 0 ? "" : $"Page {_page + 1}/{pages}");
            _prev.Interactable = _page > 0;
            _next.Interactable = _page < pages - 1;

            for (int i = 0; i < CardsPerPage; i++)
            {
                int index = _page * CardsPerPage + i;
                _cards[i].Bind(index < list.Count ? list[index] : null, now);
            }
        }

        internal MenuController MenuRef => Menu;
        internal ModRuntime RuntimeRef => Rt;
    }

    internal sealed class TrackerCard
    {
        private readonly TrackerListPage _page;
        private readonly RectTransform _root;
        private readonly Image _bg;
        private readonly Text _name, _connection, _ids, _tracking, _support;
        private readonly UIButton _role;
        private readonly UIToggle _enabled;
        private readonly UIButton _identify;
        private readonly UIButton _settings;
        private TrackerRuntime _tracker;

        public TrackerCard(TrackerListPage page, RectTransform parent, float y, float height)
        {
            _page = page;
            _root = UIFactory.Container(parent, "Card", 0f, y, MenuController.ContentWidth, height);
            _bg = UIFactory.AddImage(_root, UIStyle.Card);
            _name = UIFactory.Label(_root, "", 16f, 6f, 560f, 40f, UIStyle.FontLarge, TextAnchor.MiddleLeft);
            _connection = UIFactory.Label(_root, "", 580f, 6f, 284f, 40f, UIStyle.FontNormal, TextAnchor.MiddleRight);
            _ids = UIFactory.Label(_root, "", 16f, 46f, 850f, 30f, UIStyle.FontSmall, TextAnchor.MiddleLeft, UIStyle.TextDim);
            _tracking = UIFactory.Label(_root, "", 16f, 78f, 300f, 36f, UIStyle.FontNormal, TextAnchor.MiddleLeft);
            _support = UIFactory.Label(_root, "", 320f, 78f, 546f, 36f, UIStyle.FontSmall, TextAnchor.MiddleRight, UIStyle.TextDim);

            float by = 122f, bh = 62f;
            _role = UIFactory.Button(_root, "Role", 16f, by, 310f, bh, OnRole);
            _enabled = UIFactory.Toggle(_root, "Enabled", 336f, by, 190f, bh, OnEnabled, UIStyle.FontSmall);
            _identify = UIFactory.Button(_root, "Identify", 536f, by, 150f, bh, OnIdentify);
            _settings = UIFactory.Button(_root, "Settings", 696f, by, 168f, bh, OnSettings);
        }

        public void Bind(TrackerRuntime t, float now)
        {
            _tracker = t;
            UIFactory.SetActive(_root, t != null);
            if (t == null) return;

            ModRuntime rt = _page.RuntimeRef;
            TrackerRecord rec = t.Record;
            bool conflicted = rt.Status.IsConflicted(t);
            bool identifying = rt.Markers != null && rt.Markers.IsIdentifying(t.Key, now);

            UIFactory.SetColor(_bg, identifying ? UIStyle.CardHighlight : conflicted ? new Color(0.30f, 0.12f, 0.12f, 1f) : UIStyle.Card);
            UIFactory.SetText(_name, rec.DisplayName + (t.SettingsPersistent ? "" : "  (session only)"));
            UIFactory.SetText(_connection, rec.Connected ? "Connected" : rec.ConnectCount == 0 ? "Not detected (saved)" : "Disconnected");
            UIFactory.SetColor(_connection, rec.Connected ? UIStyle.Good : UIStyle.Bad);

            string ids = "ID " + rec.DisplayId;
            if (!string.IsNullOrEmpty(rec.ControllerType)) ids += "   SteamVR type: " + rec.ControllerType;
            if (rec.Battery >= 0f) ids += $"   Battery {rec.Battery * 100f:F0}%";
            if (rec.DuplicateSerial) ids += "   DUPLICATE SERIAL";
            UIFactory.SetText(_ids, ids);

            UIFactory.SetText(_tracking, "Pose: " + PoseValidity.Describe(t.State));
            UIFactory.SetColor(_tracking, t.State == TrackingState.Valid ? UIStyle.Good : rec.Connected ? UIStyle.Warn : UIStyle.TextDim);
            UIFactory.SetText(_support, conflicted ? "ROLE CONFLICT - not applied" : BodyRoles.SupportDescription(t.Role));
            UIFactory.SetColor(_support, conflicted ? UIStyle.Bad : UIStyle.TextDim);

            _role.SetLabel("Role: " + BodyRoles.DisplayName(t.Role) + "  v");
            if (_enabled.Value != t.Settings.Enabled) _enabled.SetValue(t.Settings.Enabled);
        }

        private void OnRole()
        {
            if (_tracker != null) _page.MenuRef.RolePicker.Show(_tracker);
        }

        private void OnEnabled(bool on)
        {
            if (_tracker == null) return;
            _tracker.Settings.Enabled = on;
            _page.RuntimeRef.OnTrackerSettingsChanged(_tracker);
            _page.MenuRef.Notify($"{_tracker.Label} {TrackerIdentity.ShortId(_tracker.Record.Serial)} {(on ? "enabled" : "disabled")}.");
        }

        private void OnIdentify()
        {
            if (_tracker == null) return;
            _page.RuntimeRef.Identify(_tracker);
        }

        private void OnSettings()
        {
            if (_tracker == null) return;
            _page.MenuRef.TrackerSettings.Select(_tracker.Key);
            _page.MenuRef.Show(_page.MenuRef.TrackerSettings);
        }
    }
}
