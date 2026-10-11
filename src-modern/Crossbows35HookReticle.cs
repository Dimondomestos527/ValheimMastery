using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    // Prediction only: no projectile/attack start, effects, damage, XP or RPC.
    // Keep vanilla hover/crosshair state intact; own a sibling-sized native sprite.
    [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
    internal static class Crossbows35HookReticle
    {
        internal enum Preview { Unavailable, Blocked, Attach }
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly Type Blocker = typeof(Player).Assembly.GetType("GrapplingBlocker");
        private static readonly Type Point = typeof(Player).Assembly.GetType("GrapplingPoint");
        private static readonly Color Ready = new Color(.45f, .85f, .45f, .85f);
        private static readonly Color Denied = new Color(.9f, .48f, .20f, .75f);
        private static readonly Color Unknown = new Color(.65f, .65f, .65f, .6f);
        private static Image Marker;
        private static Image Parent;
        private static ItemDrop.ItemData Weapon;
        private static Player Owner;
        private static Attack Aim;
        private static Projectile Template;
        private static Hud ContextHud;
        private static ZNet Session;
        private static ZNetScene Scene;
        private static float NextQuery;
        private static Preview Result;
        private static int Mask;

        private static void Postfix(Hud __instance, Player player)
        {
            if (Application.isBatchMode) return;
            try { Update(__instance, player); }
            catch { Cleanup(); } // Also release any partially constructed context.
        }

        private static void Update(Hud __instance, Player player)
        {
            if (Application.isBatchMode) return;
            if (!ReferenceEquals(__instance, Hud.instance)) return;
            ObserveContext();
            if (!Eligible(__instance, player)) { Hide(); return; }
            var item = player.GetCurrentWeapon();
            if (Parent != __instance.m_crosshair || Owner != player || !ReferenceEquals(Weapon, item))
            {
                Cleanup();
                ContextHud = __instance;
                Session = ZNet.instance;
                Scene = ZNetScene.instance;
                Parent = __instance.m_crosshair;
                Owner = player;
                Weapon = item;
                // Native clone is a managed data object; never alter the shared attack.
                Aim = item.m_shared.m_attack.Clone();
                Aim.m_character = player;
                Aim.m_baseAI = null;
                Template = Aim.m_attackProjectile != null ? Aim.m_attackProjectile.GetComponent<Projectile>() : null;
                var go = new GameObject("MasteryHook35Prediction", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.SetActive(false);
                Marker = go.GetComponent<Image>();
                go.transform.SetParent(Parent.transform, false);
                Marker.sprite = Parent.sprite;
                Marker.material = Parent.material;
                Marker.raycastTarget = false;
                Vector2 size = Parent.rectTransform.rect.size;
                Marker.rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, size.x), Mathf.Max(8f, size.y)) * 1.65f;
                Marker.rectTransform.anchoredPosition = Vector2.zero;
                NextQuery = 0f;
            }
            if (Time.unscaledTime >= NextQuery)
            {
                // At doubled flight extent query half as often: same <=400
                // segment-cast budget, without coarsening native physics steps.
                NextQuery = Time.unscaledTime + .16f * CrossbowsNativeHookScaling.RangeFactor(CrossbowsNativeHookScaling.Skill(player));
                Result = Predict(player);
            }
            Marker.color = Result == Preview.Attach ? Ready : Result == Preview.Blocked ? Denied : Unknown;
            Marker.gameObject.SetActive(true);
        }

        private static bool Eligible(Hud hud, Player player)
            => Blocker != null && Point != null && MasteryPlugin.Settings?.Enabled.Value == true &&
                ZNet.instance != null && ZNetScene.instance != null &&
                hud != null && hud.m_crosshair != null && hud.m_crosshair.gameObject.activeInHierarchy &&
                player != null && player == Player.m_localPlayer && player.m_nview?.IsOwner() == true &&
                !player.IsDead() && !player.InAttack() && !player.InDodge() && !player.IsStaggering() &&
                !player.IsSwimming() && !player.IsAttached() && !player.InPlaceMode() &&
                !InventoryGui.IsVisible() && !Menu.IsVisible() && !Console.IsVisible() && !StoreGui.IsVisible() &&
                !(TextViewer.instance != null && TextViewer.instance.IsVisible()) &&
                !TextInput.IsVisible() && !(Chat.instance != null && Chat.instance.HasFocus()) &&
                !Minimap.IsOpen() &&
                Crossbows35ReloadGuard.IsTraversalHook(player.GetCurrentWeapon()) &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Crossbows, 35);

        private static void Hide()
        {
            if (Marker != null) Marker.gameObject.SetActive(false);
            NextQuery = 0f;
            Result = Preview.Unavailable;
        }

        // Runs from Hud.Update too: native UpdateCrosshair need not run after
        // losing the player. Never let an obsolete HUD callback clear a new HUD.
        internal static void ObserveContext()
        {
            if (ReferenceEquals(ContextHud, null)) return;
            try
            {
                Observe(Hud.instance, ZNet.instance, ZNetScene.instance, Player.m_localPlayer);
                if (!ReferenceEquals(ContextHud, null) &&
                    (ContextHud == null || Owner == null || Session == null || Scene == null)) Cleanup();
            }
            catch { Cleanup(); } // Even unavailable native context cannot retain an old aim or break Hud.Update.
        }

        internal static void Observe(Hud hud, ZNet session, ZNetScene scene, Player player)
        {
            if (ReferenceEquals(ContextHud, null)) return;
            if (!ReferenceEquals(ContextHud, hud) || !ReferenceEquals(Session, session) ||
                !ReferenceEquals(Scene, scene) || !ReferenceEquals(Owner, player)) Cleanup();
        }

        internal static void ReleaseHud(Hud hud)
        { if (ReferenceEquals(ContextHud, hud)) Cleanup(); }

        internal static void ReleasePlayer(Player player)
        { if (ReferenceEquals(Owner, player)) Cleanup(); }

        internal static void ReleaseScene(ZNetScene scene)
        { if (ReferenceEquals(Scene, scene)) Cleanup(); }

        internal static void ReleaseSession(ZNet session)
        { if (ReferenceEquals(Session, session)) Cleanup(); }

        internal static void Cleanup()
        {
            // Clear every managed/session/native reference before Unity cleanup:
            // destruction is deferred and must not retain an obsolete aim/player.
            Image marker = Marker;
            Marker = null;
            Parent = null;
            Weapon = null;
            Owner = null;
            Aim = null;
            Template = null;
            ContextHud = null;
            Session = null;
            Scene = null;
            NextQuery = 0f;
            Result = Preview.Unavailable;
            Mask = 0;
            Array.Clear(Hits, 0, Hits.Length);
            try
            {
                if (marker != null)
                {
                    marker.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(marker.gameObject);
                }
            }
            catch { /* Cosmetic cleanup cannot interrupt native shutdown. */ }
        }

        private static Preview Predict(Player player)
        {
            // Reject unsupported donor changes instead of advertising inaccurate permission.
            if (Template == null || Aim == null || Template.m_rayRadius != 0f || Template.m_canHitWater ||
                Template.m_bounce || Template.m_hitMidFlight || Template.m_drag != 0f ||
                Template.m_hitOwner || Template.m_hitFriendly || Template.m_aoe != 0f ||
                Aim.m_bowDraw || Aim.m_randomVelocity || Aim.m_projectileAccuracy != 0f ||
                (int)Aim.m_attackType != 2 || !string.IsNullOrEmpty(Aim.m_attackOriginJoint) || Aim.m_useCharacterFacing ||
                Template.m_spawnOnHit == null || Template.m_spawnOnHit.GetComponent(Point) == null ||
                Template.m_spawnOnHitChance != 1f) return Preview.Unavailable;
            float dt = Time.fixedDeltaTime;
            float level = CrossbowsNativeHookScaling.Skill(player);
            float ttl = CrossbowsNativeHookScaling.FlightTime(Template.m_ttl, level);
            int budget = Mathf.FloorToInt(64f * CrossbowsNativeHookScaling.RangeFactor(level));
            if (dt <= 0f || Mathf.CeilToInt(ttl / dt) > budget) return Preview.Unavailable;
            if (Mask == 0) Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece",
                "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
            Aim.GetProjectileSpawnPoint(out Vector3 position, out Vector3 direction);
            // Native Attack.Start rotates projectile attacks to GetLookYaw before
            // firing. Predict that rotation without changing the actual transform.
            Transform body = player.transform;
            position = body.position + player.GetLookYaw() * (Quaternion.Inverse(body.rotation) * (position - body.position));
            if (Aim.m_launchAngle != 0f) direction = Quaternion.AngleAxis(Aim.m_launchAngle, Vector3.Cross(Vector3.up, direction)) * direction;
            Vector3 velocity = direction * Aim.m_projectileVel;
            Vector3 start = Template.m_doOwnerRaytest ? player.GetCenterPoint() : position;
            start.y = position.y;
            bool fromOwner = Template.m_doOwnerRaytest;
            bool insideShield = ShieldGenerator.IsInsideShield(position);
            // Projectile FixedUpdate uses semi-implicit Euler, owner start once,
            // then a half-step backwards and a 1.5-length cast on later steps.
            for (int step = 0; step < budget; step++)
            {
                velocity += Vector3.down * (Template.m_gravity * dt);
                Vector3 previous = position;
                position += velocity * dt;
                Vector3 delta = position - (fromOwner ? start : previous);
                Vector3 castStart = fromOwner ? start : previous - delta * .5f;
                int count = Physics.RaycastNonAlloc(castStart, delta.normalized, Hits, delta.magnitude * 1.5f,
                    Mask, QueryTriggerInteraction.UseGlobal);
                if (count == Hits.Length) return Preview.Unavailable; // Buffer saturation cannot yield false green.
                Sort(count);
                for (int index = 0; index < count; index++)
                {
                    Collider collider = Hits[index].collider;
                    if (collider == null) continue;
                    GameObject target = Projectile.FindHitObject(collider);
                    if (!ValidTarget(player, target?.GetComponent<IDestructible>(), Weapon.m_shared.m_dodgeable)) continue;
                    // Never execute a native hit handler in a read-only preview.
                    if (collider.GetComponent<IHitProjectile>() != null) return Preview.Unavailable;
                    if (target == null) return Preview.Unavailable;
                    if (target.GetComponent(Blocker) != null ||
                        Template.m_groundHitOnly && target.GetComponent<Heightmap>() == null ||
                        Template.m_staticHitOnly && (collider.attachedRigidbody != null || target.GetComponent<IDestructible>() != null) ||
                        target.GetComponent<Character>() != null && !Template.m_spawnOnCharacters ||
                        target.GetComponent<WearNTear>() != null && !Template.m_spawnOnWearNTear ||
                        !Template.m_spawnOnTerrain) return Preview.Blocked;
                    return Preview.Attach;
                }
                // Native shield checks occur after each segment's collision handling.
                bool shield = ShieldGenerator.IsInsideShield(position);
                if (!insideShield && shield) return Preview.Unavailable;
                insideShield = shield;
                fromOwner = false;
                // Native TTL decrements after casting and expires only when <0.
                if (ttl - (step + 1) * dt < 0f) return Preview.Blocked;
            }
            return Preview.Unavailable;
        }

        internal static bool ValidTarget(Player player, IDestructible target, bool dodgeable)
        {
            if (!(target is Character character)) return true;
            if (character == player) return false; // Native hook hitOwner=false.
            bool enemy = BaseAI.IsEnemy(player, character);
            if (!enemy && character.GetBaseAI() != null) enemy = character.GetBaseAI().IsAggravatable();
            return (player.IsPVPEnabled() || enemy) && !(dodgeable && character.IsDodgeInvincible());
        }

        private static void Sort(int count)
        {
            // Fixed buffer/insertion sort: no comparer/delegate/LINQ allocations.
            for (int i = 1; i < count; i++)
            {
                RaycastHit value = Hits[i]; int j = i - 1;
                while (j >= 0 && Hits[j].distance > value.distance) { Hits[j + 1] = Hits[j]; j--; }
                Hits[j + 1] = value;
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "Update")]
    internal static class Crossbows35HookContextPatch
    { private static void Prefix() => Crossbows35HookReticle.ObserveContext(); }

    [HarmonyPatch(typeof(Hud), "OnDestroy")]
    internal static class Crossbows35HookHudCleanupPatch
    { private static void Prefix(Hud __instance) => Crossbows35HookReticle.ReleaseHud(__instance); }

    [HarmonyPatch(typeof(Player), "OnDestroy")]
    internal static class Crossbows35HookPlayerCleanupPatch
    { private static void Prefix(Player __instance) => Crossbows35HookReticle.ReleasePlayer(__instance); }

    [HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
    internal static class Crossbows35HookSceneCleanupPatch
    { private static void Prefix(ZNetScene __instance) => Crossbows35HookReticle.ReleaseScene(__instance); }

    [HarmonyPatch(typeof(ZNet), "Shutdown", new[] { typeof(bool) })]
    internal static class Crossbows35HookSessionCleanupPatch
    { private static void Prefix(ZNet __instance) => Crossbows35HookReticle.ReleaseSession(__instance); }

    [HarmonyPatch(typeof(MasteryPlugin), "OnDestroy")]
    internal static class Crossbows35HookPluginCleanupPatch
    { private static void Prefix() => Crossbows35HookReticle.Cleanup(); }
}
