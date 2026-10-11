using UnityEngine;
namespace ValheimMastery
{
    // Cosmetic only, after durable Applied; never retries an item/permit.
    internal sealed class GoldMasterworkSuccessSound : MonoBehaviour
    {
        const string Prefab="sfx_forgeofpotential_success";
        Player player;ZNet session;ZNetScene scene;float until,next;Vector3 position;
        internal static void Prewarm()
        {if(!Application.isBatchMode&&MasteryPlugin.Settings.EnablePerkSFX.Value)NativePerkAssetResolver.Resolve(Prefab);}
        internal static void Request(Player p)
        {
            if(!GoldMasterworkPresentation.Local(p)||!MasteryPlugin.Settings.EnablePerkSFX.Value)return;
            var prefab=NativePerkAssetResolver.Resolve(Prefab);
            if(prefab!=null){PerkAudioService.PlayPrefab("crafting100_created",prefab,p.transform.position,.5f);return;}
            var cue=p.GetComponent<GoldMasterworkSuccessSound>()??p.gameObject.AddComponent<GoldMasterworkSuccessSound>();
            cue.player=p;cue.session=ZNet.instance;cue.scene=ZNetScene.instance;cue.position=p.transform.position;
            cue.until=Time.unscaledTime+2f;cue.next=Time.unscaledTime;cue.enabled=true;
        }
        void LateUpdate()
        {
            if(!GoldMasterworkPresentation.Local(player)||session!=ZNet.instance||scene!=ZNetScene.instance||!MasteryPlugin.Settings.EnablePerkSFX.Value){Stop();return;}
            if(Time.unscaledTime>=until){Stop();return;}if(Time.unscaledTime<next)return;next=Time.unscaledTime+.1f;
            var prefab=NativePerkAssetResolver.Resolve(Prefab);if(prefab==null)return;
            // Clear before playback; throttle/budget refusal cannot create a duplicate.
            var point=position;Stop();PerkAudioService.PlayPrefab("crafting100_created",prefab,point,.5f);
        }
        void Stop(){player=null;session=null;scene=null;enabled=false;}
        void OnDisable(){player=null;session=null;scene=null;}
    }
}
