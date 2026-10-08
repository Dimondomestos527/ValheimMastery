using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    internal sealed class MasterIdolWelcome:MonoBehaviour
    {
        private const string Marker="vm.idol.welcomed.v1",Rpc="VM_Idol_Welcome_v1",Particle="vfx_greydwarf_shaman_pray";
        private BaseAI Ai;private Character Creature;private ZNetView View;private MasterIdolLeash Leash;
        private bool Registered,Seen,Active,ParticlePlayed,PulsePlayed;private string Home;private float End,Next,Started,NextTrace;private long CueOwner;
        private GameObject Vfx;private string VisualStatus="not-attempted",QueuedHome;private long QueuedOwner,QueuedTicks,QueuedWorld;private ZNet QueuedSession;
        internal bool IsPresenting=>Active&&Time.unscaledTime<End;
        private void Awake(){Ai=GetComponent<BaseAI>();Creature=GetComponent<Character>();View=GetComponent<ZNetView>();}
        private MasterIdolEffectZone Source()=>View?.IsValid()==true&&Creature!=null?MasterIdolEffectZones.ResidentHome(View.GetZDO().m_uid.ToString(),Utils.GetPrefabName(gameObject)):null;
        private bool Battle=>Ai==null||Creature==null||Creature.IsDead()||Creature.InAttack()||Creature.IsStaggering()||Ai.IsAlerted()||Ai is MonsterAI monster&&(monster.m_targetCreature!=null||monster.m_targetStatic!=null);
        private void Update()
        {
            if(View?.IsValid()!=true){Stop("view-invalid");return;}
            if(!Registered){View.Register<ZPackage>(Rpc,Receive);Registered=true;}
            if(ZNet.instance?.GetWorld()==null){Stop("world-unavailable");return;}
            if(!Active&&QueuedHome!=null)
            {
                var queued=Source();
                if(!ReferenceEquals(QueuedSession,ZNet.instance)||!MasterIdolWelcomeRules.Accept(QueuedWorld,ZNet.instance.GetWorldUID(),QueuedOwner,View.GetZDO().GetOwner(),QueuedTicks,ZNet.instance.GetTime().Ticks,QueuedHome,QueuedHome)){QueuedHome=null;Seen=true;Trace("remote expired/owner/session-changed");}
                else if(queued!=null&&queued.Id!=QueuedHome){QueuedHome=null;Seen=true;}
                else if(queued!=null&&!Battle&&!Ai.IsSleeping()){Begin(QueuedHome,QueuedOwner,QueuedTicks);QueuedHome=null;}
            }
            if(Active)
            {
                var source=Source();
                if(Time.unscaledTime>=End||Battle||source?.Id!=Home||View.GetZDO().GetOwner()!=CueOwner){Stop(Time.unscaledTime>=End?"completed":Battle?"battle":source?.Id!=Home?"source-changed":"owner-changed");return;}
                Presentation(source);return;
            }
            if(!View.IsOwner()||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.25f;
            var current=Source();if(current==null||View.GetZDO().GetBool(Marker,false))return;
            Leash=Leash??GetComponent<MasterIdolLeash>();Leash?.Calm();
            if(Battle||Ai.IsSleeping()||!current.Contains(transform.position)||!MasterIdolActivity.Near(transform.position))
            {
                if(Time.unscaledTime>=NextTrace){NextTrace=Time.unscaledTime+10;Home=current.Id;Trace("pending: "+(Battle?"battle":Ai.IsSleeping()?"native-sleep":!current.Contains(transform.position)?"arriving":"distant"));}return;
            }
            // Ordinary native persistence; no once marker until the cue can actually start.
            View.GetZDO().Set(Marker,true);
            long ticks=ZNet.instance.GetTime().Ticks;
            Begin(current.Id,View.GetZDO().GetOwner(),ticks);Trace("started");
            var packet=new ZPackage();packet.Write(ZNet.instance.GetWorldUID());packet.Write(current.Id);packet.Write(ticks);
            View.InvokeRPC(ZNetView.Everybody,Rpc,packet);
        }
        private void Receive(long sender,ZPackage packet)
        {
            if(Seen||QueuedHome!=null||View?.IsValid()!=true||ZNet.instance?.GetWorld()==null||packet==null||packet.Size()>256)return;
            try
            {
                long world=packet.ReadLong();string home=packet.ReadString();long started=packet.ReadLong();
                if(packet.GetPos()!=packet.Size()||!MasterIdolWelcomeRules.Accept(world,ZNet.instance.GetWorldUID(),sender,View.GetZDO().GetOwner(),started,ZNet.instance.GetTime().Ticks,home,home))return;var source=Source();if(source!=null&&source.Id!=home)return;
                if(Battle||source==null){QueuedSession=ZNet.instance;QueuedWorld=world;QueuedHome=home;QueuedOwner=sender;QueuedTicks=started;Trace("remote pending: "+(source==null?"source-proof":"battle-state"));}else Begin(home,sender,started);
            }
            catch(Exception){/* Malformed/stale cosmetic events have no gameplay effect. */}
        }
        private void Begin(string home,long owner,long started)
        {
            VisualStatus="not-attempted";Seen=true;Home=home;CueOwner=owner;Active=true;Started=Time.unscaledTime;ParticlePlayed=PulsePlayed=false;
            float elapsed=Mathf.Max(0,(float)((ZNet.instance.GetTime().Ticks-started)/(double)TimeSpan.TicksPerSecond));
            End=Time.unscaledTime+Mathf.Max(0,MasterIdolWelcomeRules.Duration-elapsed);
        }
        internal bool Idle()
        {
            if(!Active||View?.IsOwner()!=true)return false;
            var source=Source();if(Time.unscaledTime>=End||Battle||source?.Id!=Home||View.GetZDO().GetOwner()!=CueOwner){Stop(Time.unscaledTime>=End?"completed":Battle?"battle":source?.Id!=Home?"source-changed":"owner-changed");return false;}
            Leash=Leash??GetComponent<MasterIdolLeash>();if(Leash?.Resident!=true||!source.Contains(transform.position)){Stop("outside-home");return false;}
            Ai.StopMoving();Ai.LookAt(source.Home);return true;
        }
        private void Presentation(MasterIdolEffectZone source)
        {
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null){VisualStatus="headless";return;}if(Player.m_localPlayer==null){VisualStatus="no-local-player";return;}if(!MasteryPlugin.Settings.EnablePerkProcVFX.Value){VisualStatus="vfx-disabled";return;}
            if(!PulsePlayed&&MasterIdolNetworkQuota.TryUid(Home.Split('/')[0],out long user,out uint id))
            {
                var idol=ZNetScene.instance?.FindInstance(new ZDOID(user,id));var visual=idol?.GetComponent<MasterIdolVisual>();
                if(visual!=null){visual.WelcomePulse(this,End-Time.unscaledTime);PulsePlayed=true;}
            }
            if(ParticlePlayed){if(Vfx!=null&&Vfx.GetComponent<MasterIdolWelcomeTint>()?.Owner==this)Vfx.GetComponent<MasterIdolWelcomeTint>().Fade(End-Time.unscaledTime);return;}var prefab=NativePerkAssetResolver.Resolve(Particle);if(prefab==null){VisualStatus="asset-pending";return;}
            Vfx=VfxPool.Spawn(prefab,transform.position+Vector3.up*.5f,Quaternion.identity,Mathf.Max(.15f,End-Time.unscaledTime));if(Vfx==null){VisualStatus="pool-denied";ParticlePlayed=true;return;}
            var tag=Vfx.GetComponent<MasterIdolWelcomeTint>()??Vfx.AddComponent<MasterIdolWelcomeTint>();tag.Prepare(this);
            Vfx.transform.localScale*=.3f;Vfx.SetActive(true);Vfx.GetComponent<VfxPoolBaseline>()?.RestartParticles();ParticlePlayed=true;VisualStatus="played";
        }
        private void Trace(string state){if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogInfo("[IdolTest] welcome "+state+" npc="+View?.GetZDO()?.m_uid+" home="+Home);}
        private void Stop(string reason="lifecycle")
        {
            QueuedHome=null;QueuedSession=null;if(Active)Trace(reason+" duration="+(Time.unscaledTime-Started).ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+" particles="+ParticlePlayed+" pulse="+PulsePlayed+" visual="+VisualStatus);Active=false;
            if(Vfx!=null&&Vfx.GetComponent<MasterIdolWelcomeTint>()?.Owner==this){Vfx.GetComponent<VfxPoolLease>()?.Cancel();VfxPool.Return(Particle,Vfx);}
            Vfx=null;
        }
        private void OnDisable()=>Stop();private void OnDestroy()=>Stop();
    }
    internal sealed class MasterIdolWelcomeTint:MonoBehaviour
    {
        internal MasterIdolWelcome Owner;private ParticleSystem[] Particles;private ParticleSystem.MinMaxGradient[] Colors;private int[] Counts;private Light[] Lights;private Color[] LightColors;private float[] Intensities;private readonly ParticleSystem.Particle[] Buffer=new ParticleSystem.Particle[16];
        internal void Prepare(MasterIdolWelcome owner)
        {
            Restore();Owner=owner;Particles=GetComponentsInChildren<ParticleSystem>(true);Colors=new ParticleSystem.MinMaxGradient[Particles.Length];Counts=new int[Particles.Length];
            var green=new Color(.18f,.8f,.38f);
            for(int i=0;i<Particles.Length;i++){var main=Particles[i].main;Colors[i]=main.startColor;Counts[i]=main.maxParticles;main.startColor=green;main.maxParticles=Mathf.Min(16,main.maxParticles);}
            Lights=GetComponentsInChildren<Light>(true);LightColors=new Color[Lights.Length];Intensities=new float[Lights.Length];for(int i=0;i<Lights.Length;i++){LightColors[i]=Lights[i].color;Intensities[i]=Lights[i].intensity;Lights[i].color=green;}
        }
        internal void Fade(float remaining)
        {
            float alpha=Mathf.Clamp01(remaining/.65f);var color=new Color(.18f,.8f,.38f,alpha);
            if(Particles!=null)foreach(var system in Particles)if(system!=null){var main=system.main;main.startColor=color;int count=system.GetParticles(Buffer);for(int i=0;i<count;i++)Buffer[i].startColor=color;system.SetParticles(Buffer,count);}
            if(Lights!=null)for(int i=0;i<Lights.Length;i++)if(Lights[i]!=null)Lights[i].intensity=Intensities[i]*alpha;
        }
        private void Restore()
        {
            if(Particles!=null)for(int i=0;i<Particles.Length;i++)if(Particles[i]!=null){var main=Particles[i].main;main.startColor=Colors[i];main.maxParticles=Counts[i];}
            if(Lights!=null)for(int i=0;i<Lights.Length;i++)if(Lights[i]!=null){Lights[i].color=LightColors[i];Lights[i].intensity=Intensities[i];}
            Particles=null;Lights=null;Owner=null;
        }
        private void OnDisable()=>Restore();private void OnDestroy()=>Restore();
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolWelcomeAttachPatch
    {private static void Postfix(BaseAI __instance){if(MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject))>0&&__instance.GetComponent<MasterIdolWelcome>()==null)__instance.gameObject.AddComponent<MasterIdolWelcome>();}}
    [HarmonyPatch(typeof(BaseAI),"IdleMovement")]
    internal static class MasterIdolWelcomeIdlePatch
    {private static bool Prefix(BaseAI __instance)=>MasterIdolPilotA.Get<MasterIdolWelcome>(__instance)?.Idle()!=true;}
}









