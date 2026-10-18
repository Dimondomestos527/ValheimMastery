using System;
using UnityEngine;
namespace ValheimMastery
{
    // One original native AudioClip reference, one source, one Play, no loop or internal restarts.
    internal sealed class MasterIdolResidentUniqueVoice:IDisposable
    {
        private readonly Character Actor;private readonly AudioClip Clip;private readonly ResidentContinuousVoiceRecipe Recipe;
        private readonly GameObject Root;private AudioSource Source;private bool Started,Disposed;
        private MasterIdolResidentUniqueVoice(Character actor,AudioClip clip,ResidentContinuousVoiceRecipe recipe,AudioSource template)
        {
            Actor=actor;Clip=clip;Recipe=recipe;Root=new GameObject("VM_ResidentContinuousNativeVoice");
            try
            {
                Source=Root.AddComponent<AudioSource>();Source.playOnAwake=false;Source.loop=false;Source.volume=0;
                Source.outputAudioMixerGroup=template.outputAudioMixerGroup;Source.spatialBlend=template.spatialBlend;Source.minDistance=template.minDistance;Source.maxDistance=template.maxDistance;
                Source.rolloffMode=template.rolloffMode;Source.dopplerLevel=0;Source.priority=template.priority;
                Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                UnityEngine.Object.Destroy(Root,8);
            }
            catch{Dispose();throw;}
        }
        internal static MasterIdolResidentUniqueVoice Begin(Character actor,int family,byte phrase,float maximumSeconds)
        {
            if(actor==null||family<0||family>3||phrase>9||Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null||!MasteryPlugin.Settings.EnablePerkSFX.Value)return null;
            var recipe=ResidentNativeVoiceRecipes.For(family,phrase,maximumSeconds);if(recipe==null)return null;
            string name=family==0?"sfx_greyling_idle":family==2?"sfx_greydwarf_elite_idle":"sfx_greydwarf_idle";
            var prefab=ZNetScene.instance?.GetPrefab(name)??NativeSoftVisualAssets.GetPrefab(name);var sound=prefab?.GetComponent<ZSFX>();var template=prefab?.GetComponent<AudioSource>();
            if(sound?.m_audioClips==null||sound.m_audioClips.Length!=8||template==null)return null;
            var clips=sound.m_audioClips;
            for(int i=0;i<8;i++)if(clips[i]==null||clips[i].name!="ScalyCreature_SpeechGrowl"+(i+1)||!float.IsFinite(clips[i].length)||Mathf.Abs(clips[i].length-ResidentNativeVoiceRecipes.NativeLengths[i])>.05f)return null;
            return new MasterIdolResidentUniqueVoice(actor,clips[recipe.Clip],recipe,template);
        }
        internal bool Tick(float elapsed)
        {
            if(Disposed||Root==null||Actor==null||!float.IsFinite(elapsed)||elapsed<0||elapsed>=Recipe.Duration)return false;
            Root.transform.position=Actor.GetCenterPoint()+Vector3.up*.3f;
            if(Clip.loadState!=AudioDataLoadState.Loaded){if(Clip.loadState==AudioDataLoadState.Unloaded)Clip.LoadAudioData();return true;}
            float progress=Recipe.Progress(elapsed);Source.pitch=Recipe.Pitch(progress);Source.volume=Recipe.Volume(elapsed);
            if(!Started)
            {
                Source.clip=Clip;Source.time=Recipe.Start+progress*Recipe.Seconds;Source.Play();Started=true;
                MasterIdolResidentMetrics.Count("unique.voice-continuous-native-start");return true;
            }
            return Source.isPlaying;
        }
        public void Dispose()
        {
            if(Disposed)return;Disposed=true;
            try{if(Source!=null)Source.Stop();}finally{if(Root!=null)UnityEngine.Object.Destroy(Root);}
        }
    }
}
