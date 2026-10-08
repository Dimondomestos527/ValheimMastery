using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>Debug-only, read-only inventory of assets in the running game.</summary>
    internal static class RuntimeAssetAuditService
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Execute(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length < 3) { args.Context.AddString("vm assetdump vfx|sfx|arrows|clubs|shields|spears|lines <filter>"); return; }
            string category = args.Args[2].ToLowerInvariant();
            string filter = args.Args.Length >= 4 ? args.Args[3] : "";
            if (category == "arrows") { DumpArrows(args); return; }
#if !MASTERY_RELEASE_SAFE
            if (category == "clubs") { DumpClubs(args); return; }
            if (category == "shields") { DumpShields(args); return; }
            if (category == "spears") { DumpSpears(args); return; }
            if (category == "lines") { DumpLines(args, filter); return; }
#endif
            if (category != "vfx" && category != "sfx") { args.Context.AddString("Unknown category: " + category); return; }
            List<GameObject> prefabs = ZNetScene.instance?.m_prefabs;
            if (prefabs == null) { args.Context.AddString("ZNetScene is not ready."); return; }
            int total = 0;
            HashSet<int> seen = new HashSet<int>();
            IEnumerable<GameObject> sources = prefabs.Concat(Resources.FindObjectsOfTypeAll<GameObject>());
            foreach (GameObject prefab in sources.Where(p => p != null).OrderBy(p => p.name))
            {
                if (!seen.Add(prefab.GetInstanceID()) || prefab.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int particles = prefab.GetComponentsInChildren<ParticleSystem>(true).Length;
                AudioSource[] audio = prefab.GetComponentsInChildren<AudioSource>(true);
                if (category == "vfx" ? particles == 0 : audio.Length == 0) continue;
                int lights = prefab.GetComponentsInChildren<Light>(true).Length;
                int shakers = prefab.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name.IndexOf("CamShaker", StringComparison.OrdinalIgnoreCase) >= 0);
                int flickers = prefab.GetComponentsInChildren<Component>(true).Count(c => c != null && c.GetType().Name.IndexOf("LightFlicker", StringComparison.OrdinalIgnoreCase) >= 0);
                int animators = prefab.GetComponentsInChildren<Animator>(true).Length;
                if (total++ < 60) args.Context.AddString(prefab.name + " particles=" + particles + " lights=" + lights + " flickers=" + flickers + " audio=" + audio.Length + " shaker=" + shakers + " animator=" + animators +
                    (audio.Length > 0 ? " clips=" + string.Join(",", audio.Where(a => a != null && a.clip != null).Select(a => a.clip.name).Distinct().Take(3)) : ""));
            }
            args.Context.AddString("Total matches=" + total + (total > 60 ? "; showing first 60" : ""));
        }

        internal static void Audition(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length < 4) { args.Context.AddString("vm audition vfx|sfx <prefab>"); return; }
            string category = args.Args[2].ToLowerInvariant();
            GameObject prefab = ZNetScene.instance?.GetPrefab(args.Args[3]);
            Player player = Player.m_localPlayer;
            if (prefab == null || player == null) { args.Context.AddString("Prefab or local player unavailable."); return; }
            if (category != "vfx" && category != "sfx") { args.Context.AddString("Expected vfx or sfx."); return; }
            Vector3 position = player.GetCenterPoint() + player.transform.forward * 3f;
            if (category == "sfx")
            {
                PerkAudioService.Play("audition", prefab.name, position, 0.1f);
            }
            else if (PerkNativeFeedback.PlayVfx(prefab.name, position, 1f, 5f) == null)
            { args.Context.AddString("VFX unavailable, disabled, unsafe or at visual budget: " + prefab.name); return; }
            args.Context.AddString("Requested isolated " + category + " preview: " + prefab.name + "; verify visually/by ear.");
        }

        private static void DumpArrows(Terminal.ConsoleEventArgs args)
        {
            if (ObjectDB.instance?.m_items == null) { args.Context.AddString("ObjectDB is not ready."); return; }
            int count = 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items.Where(p => p != null && p.name.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => p.name))
            {
                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                ItemDrop.ItemData.SharedData shared = drop?.m_itemData?.m_shared;
                if (shared == null) continue;
                object attack = Read(shared, "m_attack");
                object projectile = Read(attack, "m_attackProjectile");
                object velocity = Read(attack, "m_projectileVel");
                object force = Read(shared, "m_attackForce");
                HitData.DamageTypes d = shared.m_damages;
                args.Context.AddString(prefab.name + " pierce=" + d.m_pierce + " blunt=" + d.m_blunt + " slash=" + d.m_slash +
                    " fire=" + d.m_fire + " frost=" + d.m_frost + " poison=" + d.m_poison + " spirit=" + d.m_spirit +
                    " chop=" + d.m_chop + " pickaxe=" + d.m_pickaxe + " velocity=" + velocity + " force=" + force +
                    " projectile=" + (projectile as GameObject)?.name + " status=" + Read(shared, "m_attackStatusEffect"));
                count++;
            }
            args.Context.AddString("Arrow entries=" + count + "; copy this output into BOW70_ARROW_PROFILE_AUDIT.md after live runtime collection.");
        }

#if !MASTERY_RELEASE_SAFE
        private static void DumpClubs(Terminal.ConsoleEventArgs args)
        {
            if (ObjectDB.instance?.m_items == null) { args.Context.AddString("ObjectDB is not ready."); return; }
            int count = 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items.Where(p => p != null).OrderBy(p => p.name))
            {
                ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>()?.m_itemData;
                if (item?.m_shared?.m_skillType != Skills.SkillType.Clubs) continue;
                object primary = Read(item.m_shared, "m_attack");
                object secondary = Read(item.m_shared, "m_secondaryAttack");
                args.Context.AddString(prefab.name + " class=" + ClubWeaponClassService.Classify(item) +
                    " itemType=" + item.m_shared.m_itemType +
                    " primaryType=" + Read(primary, "m_attackType") +
                    " secondaryType=" + Read(secondary, "m_attackType") +
                    " primaryWidth=" + Read(primary, "m_attackRayWidth") +
                    " secondaryWidth=" + Read(secondary, "m_attackRayWidth"));
                count++;
            }
            args.Context.AddString("Club weapons=" + count + "; any UnknownClub requires explicit review before release.");
        }

        private static void DumpShields(Terminal.ConsoleEventArgs args)
        {
            if (ObjectDB.instance?.m_items == null) { args.Context.AddString("ObjectDB is not ready."); return; }
            int count = 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items.Where(p => p != null).OrderBy(p => p.name))
            {
                ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>()?.m_itemData;
                if (item?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Shield) continue;
                args.Context.AddString(prefab.name + " class=" + ShieldWeaponClassService.Classify(item) +
                    " timedBlockBonus=" + item.m_shared.m_timedBlockBonus +
                    " itemType=" + item.m_shared.m_itemType);
                count++;
            }
            args.Context.AddString("Shield items=" + count + "; classification follows vanilla BlockAttack's timed-parry boundary.");
        }

        private static void DumpSpears(Terminal.ConsoleEventArgs args)
        {
            if (ObjectDB.instance?.m_items == null) { args.Context.AddString("ObjectDB is not ready."); return; }
            int count = 0;
            foreach (GameObject prefab in ObjectDB.instance.m_items.Where(p => p != null).OrderBy(p => p.name))
            {
                ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>()?.m_itemData;
                if (item?.m_shared?.m_skillType != Skills.SkillType.Spears) continue;
                object secondary = Read(item.m_shared, "m_secondaryAttack");
                GameObject projectilePrefab = Read(secondary, "m_attackProjectile") as GameObject;
                Projectile projectile = projectilePrefab?.GetComponent<Projectile>();
                args.Context.AddString(prefab.name + " throwProjectile=" + (projectilePrefab?.name ?? "none") +
                    " respawnItemOnHit=" + (projectile?.m_respawnItemOnHit.ToString() ?? "n/a") +
                    " spawnOnCharacters=" + (projectile?.m_spawnOnCharacters.ToString() ?? "n/a") +
                    " spawnOnTerrain=" + (projectile?.m_spawnOnTerrain.ToString() ?? "n/a") +
                    " groundHitOnly=" + (projectile?.m_groundHitOnly.ToString() ?? "n/a") +
                    " secondaryType=" + Read(secondary, "m_attackType"));
                count++;
            }
            args.Context.AddString("Spear items=" + count + "; verify every throwable spear respawns its own ItemData.");
        }

        private static void DumpLines(Terminal.ConsoleEventArgs args, string filter)
        {
            int count = 0;
            foreach (LineRenderer line in Resources.FindObjectsOfTypeAll<LineRenderer>()
                .Where(line => line != null &&
                    (line.gameObject.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (line.transform.parent != null && line.transform.parent.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)))
                .OrderBy(line => line.gameObject.name))
            {
                Material material = line.sharedMaterial;
                if (count++ < 80)
                    args.Context.AddString(line.gameObject.name + " parent=" + (line.transform.parent?.name ?? "none") +
                        " material=" + (material?.name ?? "none") +
                        " shader=" + (material?.shader?.name ?? "none") +
                        " width=" + line.widthMultiplier.ToString("0.###") +
                        " points=" + line.positionCount);
            }
            args.Context.AddString("Line renderers=" + count + (count > 80 ? "; showing first 80" : "") +
                "; use a material only after runtime visual audition, never mutate its shared source.");
        }
#endif

        private static object Read(object source, string field) => source == null ? null : source.GetType().GetField(field, Fields)?.GetValue(source);
    }
}
