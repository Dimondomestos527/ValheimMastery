using System;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace ValheimMastery
{
    internal static class Ashlands35Summons
    {
        internal const string TrollName = "Troll_Summoned", RootName = "staff_greenroots_tentaroot";
        internal const string AuthorKey = "vm.ash35.author", WorldKey = "vm.ash35.world", CasterKey = "vm.ash35.caster",
            UntilKey = "vm.ash35.until", GenerationKey = "vm.ash35.generation", SpentKey = "vm.ash35.spent",
            ObjectsKey = "vm.ash35.troll.objects", HuntKey = "vm.ash35.troll.hunt";
        private sealed class Blast { internal HitData.DamageTypes Damage; internal float Radius; }
        private static readonly ConditionalWeakTable<Aoe, Blast> Blasts = new ConditionalWeakTable<Aoe, Blast>();
        private static GameObject Stage;
        private static readonly ConditionalWeakTable<Character, GameObject> BlastPrefabs = new ConditionalWeakTable<Character, GameObject>();
        private static readonly ConditionalWeakTable<Attack, object> Boosted = new ConditionalWeakTable<Attack, object>();
        internal static long Now => ZNet.instance != null && ZNet.instance.GetWorld() != null ? ZNet.instance.GetTime().Ticks : 0;
        internal static long World => ZNet.instance != null && ZNet.instance.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
        internal static bool Ready(Player caster, Skills.SkillType skill) => MasteryPlugin.Settings.Enabled.Value &&
            caster != null && !caster.IsDead() && caster.m_nview?.IsValid() == true && caster.m_nview.IsOwner() &&
            World != 0 && PerkRuntimeService.HasPerk(caster, skill, 35);
        internal static bool Prefab(Character body, string name) => body?.m_nview?.IsValid() == true &&
            body.m_nview.GetZDO().GetPrefab() == name.GetStableHashCode();
        internal static ZDO Managed(Character body)
        {
            ZDO data = body?.m_nview?.IsValid() == true ? body.m_nview.GetZDO() : null;
            return data != null && data.GetLong(AuthorKey, 0) != 0 && World != 0 && data.GetLong(WorldKey, 0) == World ? data : null;
        }
        internal static bool Friendly(Character body) => MasteryPlugin.Settings.Enabled.Value && Prefab(body, TrollName) &&
            Managed(body) is ZDO data && Ashlands35Rules.Active(Now, data.GetLong(UntilKey, 0));
        internal static void Birth(SpawnAbility ability, Character body)
        {
            Player caster = ability?.m_owner as Player; string staff = ability?.m_weapon?.m_dropPrefab?.name;
            bool troll = staff == "StaffRedTroll" && Prefab(body, TrollName);
            bool root = staff == "StaffGreenRoots" && Prefab(body, RootName);
            if ((!troll && !root) || body.m_nview.IsOwner() != true ||
                !Ready(caster, troll ? Skills.SkillType.BloodMagic : Skills.SkillType.ElementalMagic)) return;
            ZDO data = body.m_nview.GetZDO();
            if (data.GetLong(AuthorKey, 0) != 0) return;
            data.Set(AuthorKey, caster.GetPlayerID()); data.Set(CasterKey, caster.GetZDOID()); data.Set(WorldKey, World);
            data.Set(GenerationKey, 0); data.Set(SpentKey, false);
            if (troll)
            {
                if (ability.m_aoePrefab != null) BlastPrefabs.Add(body, ability.m_aoePrefab);
                MonsterAI ai = body.GetComponent<MonsterAI>();
                data.Set(ObjectsKey, ai?.m_attackPlayerObjects == true); data.Set(HuntKey, ai?.m_enableHuntPlayer == true);
                data.Set(UntilKey, Ashlands35Rules.Until(Now, Ashlands35Rules.FriendlySeconds));
            }
            else data.Set(UntilKey, 0L);
            Attach(body);
            if (troll && body.GetComponent<MonsterAI>() is MonsterAI friendlyAi)
            { friendlyAi.m_attackPlayerObjects = false; friendlyAi.m_enableHuntPlayer = false; }
        }
        internal static void Attach(Character body)
        {
            if (!Prefab(body, TrollName) && !Prefab(body, RootName)) return;
            if (body.GetComponent<Ashlands35SummonLife>() == null) body.gameObject.AddComponent<Ashlands35SummonLife>();
            if (!Application.isBatchMode && Prefab(body, RootName) && body.GetComponent<Ashlands35RootBuffVisual>() == null)
                body.gameObject.AddComponent<Ashlands35RootBuffVisual>();
        }
        internal static void Damage(Character body, ref HitData hit)
        {
            if (hit == null || body?.m_nview?.IsOwner() != true || !Friendly(body)) return;
            // Native periodic/fire/environment damage can enter ApplyDamage directly.
            // Clone: a shared hit sent to another victim must not inherit our vulnerability.
            hit = hit.Clone(); hit.m_damage.Modify(2f);
        }
        internal static void Boost(Attack attack, Humanoid body)
        {
            ZDO data = Managed(body);
            if (!MasteryPlugin.Settings.Enabled.Value || body?.m_nview?.IsOwner() != true || !Prefab(body, RootName) ||
                data == null || data.GetInt(GenerationKey, 0) != 1 || !Ashlands35Rules.Active(Now, data.GetLong(UntilKey, 0))) return;
            if (Boosted.TryGetValue(attack, out _)) return;
            Boosted.Add(attack, new object()); attack.m_damageMultiplier *= 1.5f;
        }
        internal static void Death(Character body)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || body?.m_nview?.IsOwner() != true || !(body.GetHealth() <= 0f)) return;
            ZDO data = Managed(body);
            if (data == null || data.GetBool(SpentKey, false)) return;
            bool troll = Prefab(body, TrollName), root = Prefab(body, RootName);
            if (!troll && (!root || !Ashlands35Rules.RootCanRegrow(data.GetInt(GenerationKey, 0), false))) return;
            // Mark before synchronous spawned damage/death callbacks; no despawn/unload hook.
            data.Set(SpentKey, true);
            Vector3 point = body.transform.position; Quaternion rotation = body.transform.rotation;
            if (troll) { Explode(body, point); Ashlands35Presentation.Summon(body, 1, point); }
            else Regrow(body, data, point, rotation);
        }
        private static void Regrow(Character old, ZDO source, Vector3 point, Quaternion rotation)
        {
            GameObject prefab = ZNetScene.instance?.GetPrefab(RootName);
            if (prefab == null) { MasteryPlugin.Log.LogWarning("[Ashlands35] root prefab unavailable at death; no duplicate fallback"); return; }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, point, rotation);
            Character root = instance?.GetComponent<Character>();
            if (root?.m_nview?.IsValid() != true || !root.m_nview.IsOwner())
            { if (instance != null) ZNetScene.instance.Destroy(instance); return; }
            ZDO data = root.m_nview.GetZDO();
            data.Set(AuthorKey, source.GetLong(AuthorKey, 0)); data.Set(CasterKey, source.GetZDOID(CasterKey)); data.Set(WorldKey, World);
            data.Set(GenerationKey, 1); data.Set(SpentKey, false); data.Set(UntilKey, Ashlands35Rules.Until(Now, Ashlands35Rules.RootBoostSeconds));
            data.Set(ZDOVars.s_randomSkillFactor, source.GetFloat(ZDOVars.s_randomSkillFactor, 1f));
            data.Set(ZDOVars.s_maxInstances, source.GetInt(ZDOVars.s_maxInstances, 0));
            root.SetLevel(old.GetLevel()); root.SetupMaxHealth(); root.SetHealth(root.GetMaxHealth());
            // This is one replacement body, not the staff's group SpawnAbility.
            root.m_nview.GetZDO().Persistent = source.Persistent;
            MonsterAI oldAi = old.GetComponent<MonsterAI>(), newAi = root.GetComponent<MonsterAI>();
            if (oldAi != null && newAi != null) newAi.SetFollowTarget(oldAi.GetFollowTarget());
            root.GetComponent<ZSyncAnimation>()?.SetBool("wakeup", true);
            Attach(root); Ashlands35Presentation.Summon(root, 2, point);
        }
        internal static void Prewarm(Character body)
        {
            if (!Prefab(body, TrollName) || Managed(body) == null || BlastPrefabs.TryGetValue(body, out _)) return;
            GameObject prefab = NativePerkAssetResolver.Resolve("staff_redtroll_spawn")?.GetComponent<SpawnAbility>()?.m_aoePrefab;
            if (prefab != null) BlastPrefabs.Add(body, prefab);
        }
        internal static void Shutdown() { if (Stage != null) UnityEngine.Object.Destroy(Stage); Stage = null; }
        private static void Explode(Character body, Vector3 point)
        {
            Prewarm(body);
            GameObject prefab = BlastPrefabs.TryGetValue(body, out GameObject cached) ? cached : ZNetScene.instance?.GetPrefab("staff_redtroll_aoe");
            Aoe native = prefab?.GetComponent<Aoe>();
            if (native == null) { MasteryPlugin.Log.LogWarning("[Ashlands35] native troll AoE unavailable; never respawn a troll as fallback"); return; }
            if (Stage == null) { Stage = new GameObject("VM_Ashlands35_InactiveBlast"); Stage.SetActive(false); }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, Stage.transform, false); instance.SetActive(false);
            Aoe aoe = instance.GetComponent<Aoe>();
            Blasts.Add(aoe, new Blast { Damage = native.m_damage, Radius = native.m_radius });
            instance.transform.SetParent(null, false); instance.transform.position = point;
            aoe.m_hitOwner = true; aoe.m_hitFriendly = aoe.m_hitEnemy = aoe.m_hitSame = true;
            aoe.m_skill = Skills.SkillType.None; aoe.m_canRaiseSkill = false; aoe.m_hitVariant = 7300;
            instance.SetActive(true);
            // No expiring troll attacker ID: remote native RPC damage must remain valid after its death.
            aoe.Setup(null, Vector3.zero, 0f, null, null, null); RestoreBlast(aoe);
        }
        internal static void NormalizeBlast(Character victim, ref HitData hit)
        {
            if (hit == null || hit.m_variant != 7300 || hit.HaveAttacker() || hit.m_skill != Skills.SkillType.None) return;
            hit = hit.Clone(); hit.m_variant = 1270;
            if (victim?.m_nview?.IsOwner() == true && Game.instance != null)
            {
                // Native birth has a non-player attacker. Preserve its two incoming
                // modifiers on the recipient, without a soon-to-be-destroyed attacker ID.
                hit.ApplyModifier(Game.instance.GetDifficultyDamageScalePlayer(victim.transform.position));
                hit.ApplyModifier(Game.m_enemyDamageRate);
            }
            var context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
            context.PerkId = "ashlands35_troll_death"; context.AllowSelfProc = context.AllowOtherPerkProc = false; context.XpMultiplier = 0f;
        }
        internal static void RestoreBlast(Aoe aoe)
        {
            if (aoe == null || !Blasts.TryGetValue(aoe, out Blast native)) return;
            // In a nested Fire35 death callback its general AoE setup postfix may run;
            // restore ONLY our marked death-blast to the original native values.
            aoe.m_damage = native.Damage; aoe.m_radius = native.Radius; aoe.m_owner = null;
            aoe.m_useAttackSettings = false; aoe.m_skill = Skills.SkillType.None; aoe.m_canRaiseSkill = false; aoe.m_hitVariant = 7300;
            aoe.m_hitOwner = true; aoe.m_hitFriendly = aoe.m_hitEnemy = aoe.m_hitSame = true;
        }
    }
    internal sealed class Ashlands35SummonLife : MonoBehaviour
    {
        private Character Body; private MonsterAI Ai; private float Next; private bool WasFriendly;
        private void Awake() { Body = GetComponent<Character>(); Ai = GetComponent<MonsterAI>(); }
        private void Update()
        {
            if (Time.time < Next) return; Next = Time.time + .25f;
            if (Body?.m_nview?.IsValid() != true || !Body.m_nview.IsOwner() || Ai == null || Body.IsDead()) return;
            ZDO data = Ashlands35Summons.Managed(Body);
            if (data == null || !Ashlands35Summons.Prefab(Body, Ashlands35Summons.TrollName)) return;
            Ashlands35Summons.Prewarm(Body);
            bool friendly = Ashlands35Summons.Friendly(Body);
            if (friendly) { Ai.m_attackPlayerObjects = false; Ai.m_enableHuntPlayer = false; }
            else if (WasFriendly) { Ai.m_attackPlayerObjects = data.GetBool(Ashlands35Summons.ObjectsKey, true); Ai.m_enableHuntPlayer = data.GetBool(Ashlands35Summons.HuntKey, false); }
            WasFriendly = friendly;
        }
    }
}
