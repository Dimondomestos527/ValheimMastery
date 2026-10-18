using UnityEngine;
namespace ValheimMastery
{
    // This only removes resident friendship suppression for a verified native offensive hit.
    // Scoped resident poison keeps its existing Bound/owner/home admission separately.
    internal static class MasterIdolResidentMadness
    {
        internal static bool OffensiveSpell(Aoe spell,Collider collider)
        {
            if(spell==null||collider==null||spell.m_nview?.IsValid()!=true||!spell.m_nview.IsOwner()||
                Utils.GetPrefabName(spell.gameObject)!="shaman_attack_aoe")return false;
            float damage=spell.m_damage.GetTotalDamage();
            if(!float.IsFinite(damage)||damage<=0)return false; // Runtime Setup copies attack settings; healing donor never qualifies.
            var caster=spell.m_owner;
            var target=Projectile.FindHitObject(collider)?.GetComponent<Character>();
            if(!Ashlands70Madness.OffensivePair(caster,target))return false;
            var data=caster.m_nview.GetZDO();
            // No projection is not proof of no home until the current index is ready.
            if(!MasterIdolEffectZones.Ready)return false;
            string recordedHome=data.GetString(MasterIdolNativeTaming.HomeKey,"");
            var home=MasterIdolEffectZones.ResidentHome(data.m_uid.ToString(),Utils.GetPrefabName(caster.gameObject));
            // Truly unbound creatures keep their native offense; known or pending
            // bindings still require the full current home proof below.
            if(string.IsNullOrEmpty(recordedHome)&&home==null)return true;
            return MasterIdolNativeTaming.Assigned(data)&&home!=null&&MasterIdolNativeTaming.SameHome(recordedHome,home.Id)&&
                home.Contains(caster.transform.position,15)&&home.Contains(target.transform.position,15);
        }
    }
}

