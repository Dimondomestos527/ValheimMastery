using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Native usage-menu decorator: no extra PieceCategory and no fixed category-array mutation.
    internal sealed class MasterIdolPieceList : IPieceList
    {
        private const int IdolTag = int.MaxValue;
        private readonly IPieceList _native;
        private bool _available;
        internal MasterIdolPieceList(IPieceList native) { _native = native; }
        public string DisplayName => _native.DisplayName;
        public bool ShowTags => _native.ShowTags;
        public bool CanCustomizeTags => _native.CanCustomizeTags;
        public int TagCount => _native.TagCount + (_available ? 1 : 0);
        public int TagSeparatorIndex => _native.TagSeparatorIndex;
        public string GetTagDisplayName(int index) => _available && index == _native.TagCount
            ? GoldUiLocalization.Text("Master Idols", "Майстерні ідоли") : _native.GetTagDisplayName(index);
        public int GetTagIdByIndex(int index) => _available && index == _native.TagCount ? IdolTag : _native.GetTagIdByIndex(index);
        public void UpdateAvailableTags(PieceTable table)
        {
            _native.UpdateAvailableTags(table); _available = false;
            foreach (var piece in table.m_availablePieces)
                if (MasterIdolPieces.Profile(piece) != null) { _available = true; return; }
        }
        public void GetAvailablePiecesWithTag(int tagId, PieceTable table, IList<Piece> resultOut)
        {
            if (tagId != IdolTag) { _native.GetAvailablePiecesWithTag(tagId, table, resultOut); return; }
            resultOut.Clear();
            foreach (var piece in table.m_availablePieces)
                if (MasterIdolPieces.Profile(piece) != null && !resultOut.Contains(piece)) resultOut.Add(piece);
        }
    }
    [HarmonyPatch(typeof(BuildUi), "Awake")]
    internal static class MasterIdolCategoryPatch
    {
        private static void Postfix(BuildUi __instance)
        {
            var lists = __instance.m_pieceLists;
            for (int i = 0; i < lists.Count; i++) if (lists[i] is ByUsagePieceList) lists[i] = new MasterIdolPieceList(lists[i]);
        }
    }
    // Keep every explicit recipe cost visible using native requirement rows. Restore shared layout on exit.
    internal sealed class MasterIdolRequirementRows : MonoBehaviour
    {
        private GameObject[] _native;
        private Hud _owner;
        private readonly List<GameObject> _extra = new List<GameObject>();
        private RectTransform _parent;
        private Vector2 _size;
        internal void Prepare(Hud hud, Piece piece)
        {
            if (_native == null) { _owner = hud; _native = hud.m_requirementItems; }
            if (MasterIdolPieces.Profile(piece) == null) { Restore(hud); return; }
            int needed = piece.m_resources.Length + (piece.m_craftingStation != null ? 1 : 0);
            if (_native.Length == 0 || needed <= _native.Length) { Restore(hud); return; }
            int columns = _native.Length;
            var first = _native[0].GetComponent<RectTransform>();
            if (first == null) return;
            if (_parent == null) { _parent = first.parent as RectTransform; if (_parent != null) _size = _parent.sizeDelta; }
            float rowHeight = Mathf.Max(48f, first.rect.height + 8f);
            while (_extra.Count < needed - columns)
            {
                int index = columns + _extra.Count;
                var donor = _native[index % columns];
                var clone = UnityEngine.Object.Instantiate(donor, donor.transform.parent, false);
                clone.name = "ValheimMasteryIdolRequirement" + index;
                clone.GetComponent<RectTransform>().anchoredPosition = donor.GetComponent<RectTransform>().anchoredPosition +
                    Vector2.down * (index / columns) * rowHeight;
                _extra.Add(clone);
            }
            var rows = new GameObject[needed]; Array.Copy(_native, rows, columns);
            for (int i = 0; i < _extra.Count; i++)
            { bool show = columns + i < needed; _extra[i].SetActive(show); if (show) rows[columns + i] = _extra[i]; }
            hud.m_requirementItems = rows;
            if (_parent != null) _parent.sizeDelta = _size + Vector2.up * ((needed - 1) / columns) * rowHeight;
        }
        private void Restore(Hud hud)
        {
            if (_native != null) hud.m_requirementItems = _native;
            foreach (var row in _extra) if (row != null) row.SetActive(false);
            if (_parent != null) _parent.sizeDelta = _size;
        }
        private void OnDisable() { if (_owner != null) Restore(_owner); }
    }
    [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
    internal static class MasterIdolRequirementsPatch
    {
        private static void Prefix(Hud __instance, Piece piece) =>
            (__instance.GetComponent<MasterIdolRequirementRows>() ?? __instance.gameObject.AddComponent<MasterIdolRequirementRows>()).Prepare(__instance, piece);
    }
}
