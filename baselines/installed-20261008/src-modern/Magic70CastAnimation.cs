#if MASTERY_MAGIC70_EXPERIMENT
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Cosmetic spells do not own a native Attack. In particular the frost
    // staff's looping primary cannot terminate without Attack.Stop(). Use the
    // real, non-looping shield cast and a bounded owner-synchronized exit.
    internal sealed class Magic70CastAnimation : MonoBehaviour
    {
        private Player Owner;
        private Attack PreviousAttack;
        private ItemDrop.ItemData Weapon;
        private string Trigger;
        private int Rest;
        private float Started, Deadline, SuppressOldEventsUntil;
        private bool Active, Entered;
        internal static bool Casting(Humanoid actor)
        {
            var pose = actor != null ? actor.GetComponent<Magic70CastAnimation>() : null;
            return pose != null && (pose.Active || Time.time < pose.SuppressOldEventsUntil) &&
                (actor.m_currentAttack == null || actor.m_currentAttack == pose.PreviousAttack);
        }

        internal static void Play(Player player)
        {
            if (player != Player.m_localPlayer || player.m_animator == null) return;
            Attack cast = ObjectDB.instance?.GetItemPrefab("StaffShield")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attack;
            if (cast == null || cast.m_loopingAttack || string.IsNullOrEmpty(cast.m_attackAnimation))
            {
                MasteryPlugin.Log.LogWarning("[Magic70Cast] Native finite shield animation unavailable; skipping cosmetic pose.");
                return; // An absent cosmetic must never lock gameplay.
            }
            var pose = player.GetComponent<Magic70CastAnimation>() ?? player.gameObject.AddComponent<Magic70CastAnimation>();
            pose.Finish();
            pose.Owner = player; pose.PreviousAttack = player.m_currentAttack; pose.Weapon = player.GetCurrentWeapon();
            pose.Trigger = cast.m_attackAnimation;
            pose.Rest = player.m_animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            pose.Started = Time.time; pose.Deadline = Time.time + 1.8f;
            pose.Entered = false; pose.Active = true;
            player.m_zanim.SetTrigger(pose.Trigger);
        }

        private void LateUpdate()
        {
            if (!Active) return;
            if (Owner == null || Owner.IsDead() || Owner.IsTeleporting() || Owner.IsStaggering() ||
                Owner.InDodge() || Owner.GetCurrentWeapon() != Weapon ||
                (Owner.m_currentAttack != null && Owner.m_currentAttack != PreviousAttack) || Time.time >= Deadline)
            { Finish(); return; }
            Animator animator = Owner.m_animator;
            if (animator == null) { Finish(); return; }
            bool attack = animator.GetCurrentAnimatorStateInfo(0).tagHash == Humanoid.s_animatorTagAttack ||
                (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).tagHash == Humanoid.s_animatorTagAttack);
            if (attack) Entered = true;
            // Leave naturally if possible; the deadline also handles missing
            // animation events, interrupted transitions and modded controllers.
            if (Entered && !attack && Time.time - Started > .15f) Finish();
        }

        private void Finish()
        {
            if (!Active) return;
            Active = false;
            SuppressOldEventsUntil = Time.time + .15f;
            if (Owner == null || Owner.m_animator == null) return;
            Owner.m_animator.ResetTrigger(Trigger);
            if (Owner.m_currentAttack != null && Owner.m_currentAttack != PreviousAttack) return; // Never cancel a new real attack.
            Owner.m_zanim.SetTrigger("attack_abort"); // Native looping-attack exit, replicated to observers.
            bool stuckInOwnPose = Owner.m_animator.GetCurrentAnimatorStateInfo(0).tagHash == Humanoid.s_animatorTagAttack;
            if (stuckInOwnPose && Rest != 0 && !Owner.IsDead() && !Owner.IsStaggering() && !Owner.InDodge())
                Owner.m_animator.CrossFade(Rest, .12f, 0);
        }
        private void OnDisable() => Finish();
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class Magic70CosmeticCastEventPatch
    { private static bool Prefix(Humanoid __instance) => !Magic70CastAnimation.Casting(__instance); }
}
#endif
