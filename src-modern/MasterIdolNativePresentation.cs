using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolNativeBody:MonoBehaviour
    {
        private const int Wake=unchecked((int)2808015892u),Movement=unchecked((int)3885065915u);
        private Animator Animator;private bool Applied;private float Until;private Character Creature;private ZNetView View;
        internal void Begin(float elapsed,float remaining)
        {
            Creature=GetComponent<Character>();View=GetComponent<ZNetView>();Animator=GetComponentInChildren<Animator>();if(Animator==null||Creature==null||MasterIdolResidentWork.Busy(Creature)||GetComponent<MasterIdolResidentPresentation>()?.Playing==true||GetComponent<BaseAI>()?.IsSleeping()==true||GetComponent<MasterIdolSocialSeats>()?.Active==true||Creature.IsDead()||Creature.InAttack()||Creature.InDodge()||Creature.InEmote()||Creature.IsStaggering()||!Animator.HasState(0,Wake))return;
            string family=Utils.GetPrefabName(gameObject);float duration=family=="Greyling"?2:2.333333f;if(elapsed>=duration)return;
            Animator.Play(Wake,0,Mathf.Clamp01(elapsed/duration));Applied=true;Until=Time.unscaledTime+Mathf.Min(remaining,duration-elapsed);
        }
        internal void Stop(){if(Applied&&Animator!=null&&Creature!=null&&!MasterIdolResidentWork.Busy(Creature)&&GetComponent<MasterIdolResidentPresentation>()?.Playing!=true&&!Creature.IsDead()&&!Creature.IsStaggering()&&!Creature.InAttack()&&!Creature.InDodge()&&!Creature.InEmote()&&(Animator.GetCurrentAnimatorStateInfo(0).fullPathHash==Wake||Animator.GetNextAnimatorStateInfo(0).fullPathHash==Wake))Animator.CrossFade(Movement,.15f,0);Applied=false;}
        private void Update(){if(!Applied)return;if(Time.unscaledTime>=Until||Creature==null||MasterIdolResidentWork.Busy(Creature)||GetComponent<MasterIdolResidentPresentation>()?.Playing==true||Creature.IsDead()||Creature.InAttack()||Creature.InDodge()||Creature.InEmote()||Creature.IsStaggering()||GetComponent<BaseAI>()?.IsSleeping()==true||GetComponent<MasterIdolSocialSeats>()?.Active==true||View?.IsValid()!=true||GetComponent<MasterIdolWelcome>()?.IsPresenting!=true)Stop();}
        private void OnDisable()=>Stop();private void OnDestroy()=>Stop();
    }
    internal static class MasterIdolNativePresentation
    {
        internal static void Refilled(Fireplace fire,float before,float after)
        {
            if(fire==null||!(after>before)||fire.m_nview?.IsValid()!=true||!fire.m_nview.IsOwner())return;
            // Actual native nonpersistent effect donors replicate from their single authoritative creator.
            // Cosmetic failure cannot roll back the already committed free native fuel update.
            try{var source=fire.m_fuelAddedEffects?.m_effectPrefabs;if(source==null)return;var selected=new System.Collections.Generic.List<EffectList.EffectData>();foreach(var effect in source){if(!effect.m_enabled||effect.m_prefab==null)continue;bool audio=effect.m_prefab.GetComponentInChildren<UnityEngine.AudioSource>(true)!=null;if(audio?MasteryPlugin.Settings.EnablePerkSFX.Value:MasteryPlugin.Settings.EnablePerkProcVFX.Value)selected.Add(effect);}if(selected.Count>0)new EffectList{m_effectPrefabs=selected.ToArray()}.Create(fire.transform.position,fire.transform.rotation,null,1,-1,default);MasterIdolPerf.Count("lighting.native-cues");}catch(System.Exception error){MasteryPlugin.Log?.LogWarning("[MasterIdols] Refill cue: "+error.Message);}
        }
    }
}
