using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterIdolPieces
    {
        private static ZNetScene Scene;
        private static readonly List<GameObject> Templates = new List<GameObject>();
        private static GameObject Staging;
        internal static CraftingStation Workbench => ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
        internal static MasterIdolProfile Profile(Piece piece) => piece == null ? null :
            MasterIdolProfiles.FromPrefab(Utils.GetPrefabName(piece.gameObject));
        internal static void Register(ZNetScene scene)
        {
            if (scene == null || Scene == scene) return;
            Scene = scene; Templates.Clear();
            var native = scene.GetPrefab("guard_stone");
            if (native == null) { MasteryPlugin.Log.LogWarning("[MasterIdols] Native guard_stone unavailable; no substitute registered."); return; }
            Staging = new GameObject("VM_InactiveMasterIdolTemplates"); Staging.SetActive(false);
            Staging.transform.SetParent(scene.transform, false);
            foreach (var profile in MasterIdolProfiles.All)
            {
                if (scene.m_namedPrefabs.ContainsKey(profile.Prefab.GetStableHashCode())) continue;
                var prefab = UnityEngine.Object.Instantiate(native, Staging.transform, false); prefab.name = profile.Prefab;
                var piece = prefab.GetComponent<Piece>(); var view = prefab.GetComponent<ZNetView>();
                if (piece == null || view == null) { UnityEngine.Object.Destroy(prefab); continue; }
                var visual = prefab.AddComponent<MasterIdolVisual>();
                foreach (var ward in prefab.GetComponentsInChildren<PrivateArea>(true))
                {
                    if (visual.NativeModel == null) visual.NativeModel = ward.m_model;
                    if (ward.m_enabledEffect != null && ward.m_enabledEffect != prefab && ward.m_enabledEffect.transform.IsChildOf(prefab.transform))
                    {
                        visual.NativeGlow = ward.m_enabledEffect;
                        foreach (var audio in visual.NativeGlow.GetComponentsInChildren<AudioSource>(true)) { audio.Stop(); audio.playOnAwake = false; audio.enabled = false; }
                        foreach (var sound in visual.NativeGlow.GetComponentsInChildren<ZSFX>(true)) sound.enabled = false;
                    }
                    visual.ActivateSound = ward.m_activateEffect;
                    HideOwned(ward.m_enabledEffect, prefab); HideOwned(ward.m_connectEffect, prefab); HideOwned(ward.m_inRangeEffect, prefab);
                    if (ward.m_areaMarker != null) HideOwned(ward.m_areaMarker.gameObject, prefab);
                    UnityEngine.Object.DestroyImmediate(ward);
                }
                piece.m_name = GoldUiLocalization.Text(profile.English, profile.Ukrainian);
                piece.m_description = GoldUiLocalization.Text("Two active idols of different biomes per workshop network. Costs 250 Favor, no cooldown. Dismantling returns 187.5 Favor to the original payer. Indestructible to damage. Requires Crafting 100 and a nearby workbench. ",
                    "Два активні ідоли різних країв на мережу майстерні. Ціна: 250 Favor, без кулдауну. Розбір повертає 187,5 Favor тому, хто сплатив. Не зазнає пошкоджень. Потрібні Ремесло 100 і верстак поруч. Матеріали: до 100 ваги. ");
                piece.m_description += "\n"+MasterIdolEffectsSummary.For(profile.Id);
                piece.m_category = Piece.PieceCategory.Misc; piece.m_usage = Piece.UsageTagFlags.Decor;
                piece.m_canBeRemoved=true; piece.m_comfort = 0; piece.m_craftingStation = Workbench; piece.m_resources = Array.Empty<Piece.Requirement>();
                piece.m_enabled = false; view.m_persistent = true;
                prefab.AddComponent<MasterIdolPiece>();
                Templates.Add(prefab); scene.m_prefabs.Add(prefab); scene.m_namedPrefabs.Add(profile.Prefab.GetStableHashCode(), prefab);
            }
            RefreshRecipes();
        }
        private static void HideOwned(GameObject effect, GameObject root)
        { if (effect != null && effect != root && effect.transform.IsChildOf(root.transform)) effect.SetActive(false); }
        internal static void RefreshRecipes()
        {
            if (ObjectDB.instance == null) return;
            var table = ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
            foreach (var prefab in Templates)
            {
                if (prefab == null) continue;
                var piece = prefab.GetComponent<Piece>(); var profile = MasterIdolProfiles.FromPrefab(prefab.name);
                piece.m_craftingStation = Workbench;
                if (piece.m_resources.Length == 0)
                {
                    var resources = new List<Piece.Requirement>();
                    foreach (var resource in profile.Resources)
                    {
                        var item = ObjectDB.instance.GetItemPrefab(resource.Prefab)?.GetComponent<ItemDrop>();
                        if (item == null) { resources.Clear(); break; }
                        resources.Add(new Piece.Requirement { m_resItem = item, m_amount = resource.Amount, m_amountPerLevel = 0, m_recover = true });
                    }
                    if (resources.Count == profile.Resources.Count)
                    {
                        int[] requested = resources.ConvertAll(r => r.m_amount).ToArray();
                        float[] weights = resources.ConvertAll(r => r.m_resItem.m_itemData.m_shared.m_weight).ToArray();
                        int[] amounts = MasterIdolRecipeBudget.Fit(requested, weights);
                        if (amounts != null)
                        {
                            for (int i = 0; i < amounts.Length; i++) resources[i].m_amount = amounts[i];
                            piece.m_resources = resources.ToArray();
                        }
                    }
                }
                // Server definitions must not depend on the listen host's own unlock.
                piece.m_enabled = GoldCraftingService.Enabled && piece.m_craftingStation != null && piece.m_resources.Length != 0;
                bool visible = piece.m_enabled && GoldCraftingService.Unlocked;
                if (table == null) continue;
                if (visible && !table.m_pieces.Contains(prefab)) table.m_pieces.Add(prefab);
                if (!visible) table.m_pieces.Remove(prefab);
            }
        }
        internal static void Suspend()
        {
            // Unity destroyed objects are not CLR null; native lookup also requires its live managed dictionary.
            var db=ObjectDB.instance;
            var hammer=db!=null&&db.m_itemByHash!=null?db.GetItemPrefab("Hammer"):null;
            var table=hammer!=null?hammer.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces:null;
            int missing=0;
            foreach (var prefab in Templates)
            {
                if (prefab == null) continue;
                var piece=prefab.GetComponent<Piece>();if(piece!=null)piece.m_enabled=false;else missing++;
                if(table!=null)table.m_pieces?.Remove(prefab);
            }
            if(missing!=0)MasteryPlugin.Log?.LogInfo("[MasterIdols] Cleanup pieces.suspend: templates without Piece="+missing+"; late native teardown tolerated.");
        }
    }
    internal sealed class MasterIdolPiece : MonoBehaviour, IPlaced, Hoverable, Interactable
    {
        public bool Interact(Humanoid user, bool hold, bool alt) => MasterIdolInteraction.Request(user, GetComponent<ZNetView>()?.GetZDO(), hold);
        private void Start() {var zdo=GetComponent<ZNetView>()?.GetZDO();if(ZNet.instance?.IsServer()==true)MasterIdolWorldIndex.Observe(zdo);}
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
        public void OnPlaced() => MasterIdolPlacement.Stamp(GetComponent<ZNetView>()?.GetZDO());
        public string GetHoverName() => GetComponent<Piece>()?.m_name ?? "Ідол";
        public float GetHoverOffset() => 1f;
        public string GetHoverText()
        {
            var record = GetComponent<ZNetView>()?.GetZDO();
            if (record == null) return GetHoverName();
            string state = !record.GetBool(MasterIdolWorldRegistry.ActiveKey, false) ? "Благословення ідола ще не закріпилося у світі" :
                record.GetBool(MasterIdolWorldRegistry.BoundKey, false) ? "Верстак поруч" : "Неактивний: поза радіусом верстака";
            if(record.GetBool(MasterIdolWorldRegistry.QuotaKey,false))state="Призупинений: благословення цього краю вже діє або мережа має два ідоли";
            bool on = MasterIdolToggleRules.On(record.GetInt(MasterIdolWorldRegistry.SwitchKey, 0));
            return Localization.instance.Localize(GetHoverName() + "\nДва активні ідоли різних країв на мережу майстерні\n" + state +
                (on ? "\nУвімкнений" : "\nВимкнений") + "\n[<color=yellow><b>$KEY_Use</b></color>] " +
                (on ? "Вимкнути" : "Увімкнути") + "\n" + MasterIdolEffectsSummary.For(MasterIdolPieces.Profile(GetComponent<Piece>())?.Id));
        }
    }
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class MasterIdolPrefabPatch
    { [HarmonyPriority(Priority.Last)] private static void Postfix(ZNetScene __instance) => MasterIdolPieces.Register(__instance); }
}
