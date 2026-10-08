using UnityEngine;

namespace ValheimMastery
{
    internal sealed class ShadowStrikeGuarantee : MonoBehaviour
    {
        private Player _player;
        private Character _target;
        private HitData.DamageTypes _damage;
        private float _stagger;
        private float _push;
        private float _backstab;
        private float _fireAt;
        private bool _resolved;

        internal static void Schedule(Player player, Character target, ItemDrop.ItemData weapon, Attack attack)
        {
            if (player == null || target == null || weapon?.m_shared == null || attack == null) return;
            GameObject root = new GameObject("ValheimMastery_ShadowStrikeGuarantee");
            ShadowStrikeGuarantee runner = root.AddComponent<ShadowStrikeGuarantee>();
            runner._player = player;
            runner._target = target;
            runner._damage = weapon.GetDamage();
            float skillFactor = player.GetSkills().GetRandomSkillFactor(Skills.SkillType.Knives);
            runner._damage.Modify(Mathf.Max(0f, attack.m_damageMultiplier * skillFactor));
            runner._stagger = Mathf.Max(0f, attack.m_staggerMultiplier);
            runner._push = Mathf.Max(0f, attack.m_forceMultiplier * 10f);
            runner._backstab = weapon.m_shared.m_backstabBonus;
            runner._fireAt = Time.time + 0.30f;
            Destroy(root, 1.5f);
        }

        private void Update()
        {
            if (_resolved || Time.time < _fireAt) return;
            // Resolve once even if a damage/VFX callback throws: never retry a hit
            // on following frames while Unity is waiting to destroy this runner.
            _resolved = true;
            try
            {
                if (_player == null || _player.IsDead() || _target == null || _target.IsDead()) return;
                HitData hit = new HitData();
                hit.m_skill = Skills.SkillType.Knives;
                hit.m_point = _target.GetCenterPoint();
                hit.m_dir = (_target.GetCenterPoint() - _player.GetCenterPoint()).normalized;
                hit.m_damage = _damage;
                hit.m_staggerMultiplier = _stagger;
                hit.m_pushForce = _push;
                hit.m_backstabBonus = _backstab;
                hit.SetAttacker(_player);
                MasteryAttackTagService.Add(hit, MasteryAttackTag.Secondary | MasteryAttackTag.ShadowStrike);
                PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
                context.IsPerkGenerated = true;
                context.PerkId = "knives_35_shadow_strike";
                context.AllowSelfProc = true;
                context.AllowOtherPerkProc = false;
                _target.Damage(hit);
                AssassinBlinkVisualService.PlayImpact(hit.m_point);
            }
            finally { Destroy(gameObject); }
        }
    }
}
