using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Local cosmetic rig only. Native actor/root/bones/AI/collision remain untouched.
    internal sealed class MasterIdolResidentBirthPresentation:MonoBehaviour
    {
        private const float Duration=10f;
        private const float Admission=8f,Preparation=1.5f,OwnedLifetime=12f;
        internal const float SpawnDistanceSquared=1024f;
        private static readonly Dictionary<string,GameObject> Templates=new Dictionary<string,GameObject>(StringComparer.Ordinal);
        private static ZNetScene TemplateScene;
        private static ObjectDB WarmDatabase;
        private static ulong TemplateGeneration;
        private static MasterIdolResidentBirthPresentation Current;
        private static readonly List<MasterIdolResidentBirthPresentation> Waiting=new List<MasterIdolResidentBirthPresentation>(4);
        private static readonly Dictionary<string,long> Seen=new Dictionary<string,long>(StringComparer.Ordinal);
        private static GameObject WoodSound,StoneSound;
        private static readonly HashSet<AudioClip> RequestedAudio=new HashSet<AudioClip>();
        internal static int GraphicsCount=>Current!=null&&(Current.Active||Current.Pending)?1:0;
        internal static bool Playing(Character actor){var cue=actor?.GetComponent<MasterIdolResidentBirthPresentation>();return cue!=null&&cue.Active&&cue.Suppressed;}
        private Character Actor;private ZNetView View;private ZDO Data;private BaseAI Ai;private bool Tried,Active,Pending,Suppressed,Queued,StonePlayed;
        private ZNet Session;private ZDOMan Manager;private ulong Generation;private long Owner,Born;private ushort Revision;private string Nonce;
        private float InitialHealth,NextSight,NextAttempt,StartedAt,QueueUntil,PrepareUntil,SoundUntil;private Vector3 Origin;private GameObject Body;
        private readonly List<GameObject> Atmosphere=new List<GameObject>(2);
        private readonly List<GameObject> Sounds=new List<GameObject>(2);
        private readonly List<GameObject> OwnedVisuals=new List<GameObject>(9);
        private readonly List<GameObject> Fragments=new List<GameObject>(6);
        private readonly List<Vector3> Starts=new List<Vector3>(6);
        private readonly List<Renderer> Originals=new List<Renderer>(6);
        private readonly List<bool> OriginalEnabled=new List<bool>(6);
        private void Awake(){Actor=GetComponent<Character>();View=GetComponent<ZNetView>();Ai=GetComponent<BaseAI>();}
        internal static void Attach(Character actor)
        {
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)return;
            if(actor!=null&&MasterIdolForestGuardState.ModuleBorn(actor.m_nview?.GetZDO())&&actor.GetComponent<MasterIdolResidentBirthPresentation>()==null)
                actor.gameObject.AddComponent<MasterIdolResidentBirthPresentation>();
        }
        internal static void Stop(Character actor)=>actor?.GetComponent<MasterIdolResidentBirthPresentation>()?.End();
        internal static bool TrySpawnView(out Camera camera,out Vector3 playerPosition)
        {
            camera=null;playerPosition=default;var player=Player.m_localPlayer;var view=GameCamera.instance?.m_camera;
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!Application.isFocused||Game.IsPaused()||
                player==null||player.IsDead()||player.IsTeleporting()||player.InGhostMode()||player.IsDebugFlying()||view==null||!view.isActiveAndEnabled)return false;
            camera=view;playerPosition=player.transform.position;return true;
        }
        internal static bool SpawnVisible(Camera camera,Vector3 observer,Vector3 groundPoint)
        {
            if(camera==null||camera!=GameCamera.instance?.m_camera||!camera.isActiveAndEnabled||!((groundPoint-observer).sqrMagnitude<=SpawnDistanceSquared))return false;
            var seed=groundPoint+Vector3.up*.1f;var body=groundPoint+Vector3.up*.9f;
            var first=camera.WorldToViewportPoint(seed);var second=camera.WorldToViewportPoint(body);
            if(!MasterIdolVisibilityRules.Viewport(first.x,first.y,first.z)||!MasterIdolVisibilityRules.Viewport(second.x,second.y,second.z))return false;
            int mask=LayerMask.GetMask("piece","static_solid","terrain");
            return !Physics.Linecast(camera.transform.position,seed,out _,mask,QueryTriggerInteraction.Ignore)&&
                !Physics.Linecast(camera.transform.position,body,out _,mask,QueryTriggerInteraction.Ignore);
        }
        internal static bool FreshIdleHold(Character actor)
        {
            var view=actor?.m_nview;var data=view?.GetZDO();var net=ZNet.instance;
            if(!GoldCraftingService.Enabled||!MasterIdolLoadIdentity.LoadReady||net?.GetWorld()==null||actor==null||view?.IsValid()!=true||data==null||!view.IsOwner()||
                !ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)||!MasterIdolForestGuardState.ModuleBorn(data))return false;
            float age=(float)((net.GetTime().Ticks-data.GetLong(MasterIdolForestGuardState.BornKey,0))/(double)TimeSpan.TicksPerSecond);
            var ai=actor.GetBaseAI();float health=actor.GetHealth();var cue=actor.GetComponent<MasterIdolResidentBirthPresentation>();
            if(!float.IsFinite(age)||age<0||age>OwnedLifetime||!float.IsFinite(health)||health<=0||ai==null||!(ai.m_timeSinceHurt>=OwnedLifetime)||
                actor.IsDead()||actor.InAttack()||actor.InDodge()||actor.IsStaggering()||actor.InEmote()||ai.IsSleeping()||ai.IsAlerted()||
                ai is MonsterAI monster&&(monster.m_targetCreature!=null||monster.m_targetStatic!=null)||
                cue!=null&&(cue.Tried&&!cue.Active&&!cue.Pending||cue.Active&&health<cue.InitialHealth))return false;
            return MasterIdolActivity.Near(actor.transform.position,32);
        }
        private bool Context()
        {
            return Actor!=null&&Ai!=null&&View?.IsValid()==true&&!Actor.IsDead()&&GoldCraftingService.Enabled&&
                MasterIdolLoadIdentity.LoadReady&&ZNet.instance?.GetWorld()!=null&&
                ReferenceEquals(ZDOMan.instance?.GetZDO(View.GetZDO().m_uid),View.GetZDO())&&MasterIdolForestGuardState.ModuleBorn(View.GetZDO());
        }
        private bool Local(bool sight)
        {
            var player=Player.m_localPlayer;var camera=GameCamera.instance?.m_camera;
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null||!Application.isFocused||
                player==null||player.IsDead()||player.IsTeleporting()||player.InGhostMode()||player.IsDebugFlying()||
                camera==null||!camera.isActiveAndEnabled||Game.IsPaused()||
                (player.transform.position-transform.position).sqrMagnitude>1024)return false;
            Vector3 point=transform.position+Vector3.up*.9f;var screen=camera.WorldToViewportPoint(point);
            if(!MasterIdolVisibilityRules.Viewport(screen.x,screen.y,screen.z))return false;
            if(!sight)return true;
            return !Physics.Linecast(camera.transform.position,point,out var hit,LayerMask.GetMask("piece","static_solid","terrain"),QueryTriggerInteraction.Ignore)||hit.collider?.GetComponentInParent<Character>()==Actor;
        }
        private bool Peace()
        {
            return !Actor.InAttack()&&!Actor.InDodge()&&!Actor.IsStaggering()&&!Actor.InEmote()&&!Ai.IsSleeping()&&
                !MasterIdolSocialSeats.Battle(Ai,Actor)&&!MasterIdolResidentWork.Busy(Actor)&&
                GetComponent<MasterIdolResidentPresentation>()?.Playing!=true&&!MasterIdolResidentSceneAnimation.Playing(Actor)&&GetComponent<MasterIdolWelcome>()?.IsPresenting!=true&&
                Actor.GetHealth()>=InitialHealth&&(transform.position-Origin).sqrMagnitude<=.25f;
        }
        private static void TemplatesCurrent()
        {
            if(TemplateScene==ZNetScene.instance&&TemplateGeneration==MasterIdolLoadIdentity.Generation)return;
            ClearOwned();TemplateScene=ZNetScene.instance;TemplateGeneration=MasterIdolLoadIdentity.Generation;
        }
        internal static void ClearOwned()
        {
            Current?.End();
            foreach(var cue in Waiting.ToArray())cue?.End();Waiting.Clear();Seen.Clear();RequestedAudio.Clear();WoodSound=StoneSound=null;
            foreach(var template in Templates.Values)if(template!=null)Destroy(template);
            Templates.Clear();TemplateScene=null;WarmDatabase=null;
        }
        internal static void Warm()
        {
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||ZNetScene.instance==null||ObjectDB.instance==null)return;
            TemplatesCurrent();
            WoodSound=ZNetScene.instance.GetPrefab("sfx_tree_hit")??NativeSoftVisualAssets.Get<GameObject>("Assets/world/Props/FirTree/fx/sfx_tree_hit.prefab");
            StoneSound=ZNetScene.instance.GetPrefab("sfx_greydwarf_stone_hit")??NativeSoftVisualAssets.Get<GameObject>("Assets/Characters/GreyDwarf/fx/sfx_greydwarf_stone_hit.prefab");
            WarmSound(WoodSound);WarmSound(StoneSound);
            EffectTemplate("cloud","Assets/Characters/GreyDwarf/fx/vfx_greydwarf_death.prefab","soft cloud");
            EffectTemplate("dust","Assets/Effects/vfx_SawDust.prefab","smokepoff");
            if(WarmDatabase==ObjectDB.instance)return;
            foreach(string family in MasterIdolForestRules.Families)if(ZNetScene.instance.GetPrefab(family)==null)return;
            foreach(string item in new[]{"AncientSeed","Wood","Stone","Resin"})if(ObjectDB.instance.GetItemPrefab(item)==null)return;
            foreach(string family in MasterIdolForestRules.Families)if(BodyTemplate(family)==null)return;
            foreach(string item in new[]{"AncientSeed","Wood","Stone","Resin"})if(FragmentTemplate(item)==null)return;
            WarmDatabase=ObjectDB.instance;
        }
        private static void WarmSound(GameObject prefab)
        {
            var clips=prefab?.GetComponent<ZSFX>()?.m_audioClips;if(clips==null||clips.Length==0)return;var clip=clips[0];
            if(clip!=null&&clip.loadState==AudioDataLoadState.Unloaded&&RequestedAudio.Count<2&&RequestedAudio.Add(clip))clip.LoadAudioData();
        }
        private static GameObject EffectTemplate(string name,string path,string child)
        {
            string key="vfx_VM_BF_BirthAtmosphere_"+name;if(Templates.TryGetValue(key,out var cached))return cached;
            var donor=NativeSoftVisualAssets.Get<GameObject>(path)?.transform.Find(child);
            if(donor==null||donor.childCount!=0||donor.GetComponents<Component>().Length!=3||donor.GetComponent<ParticleSystem>()==null||donor.GetComponent<ParticleSystemRenderer>()==null)return null;
            var root=new GameObject(key);root.SetActive(false);
            try
            {
                var own=Instantiate(donor.gameObject,root.transform,false);own.transform.localPosition=Vector3.zero;
                var ps=own.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=ps.main;main.playOnAwake=false;main.loop=false;main.duration=Duration;main.startLifetime=.55f;main.startSpeed=.3f;main.startSize=name=="cloud"?.8f:.3f;main.maxParticles=12;
                main.startColor=name=="cloud"?new Color(.28f,.31f,.23f,.42f):new Color(.35f,.27f,.17f,.5f);
                var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,4),new ParticleSystem.Burst(1.8f,4),new ParticleSystem.Burst(6,4)});
                var collision=ps.collision;collision.enabled=false;var trigger=ps.trigger;trigger.enabled=false;var sub=ps.subEmitters;sub.enabled=false;
                RenderSettings(own.GetComponent<ParticleSystemRenderer>());Templates.Add(key,root);return root;
            }
            catch{Destroy(root);return null;}
        }
        private void Sound(GameObject prefab,float gain)
        {
            if(prefab==null||!MasteryPlugin.Settings.EnablePerkSFX.Value||PerkAudioService.ActiveSounds>=32)return;
            var source=prefab.GetComponent<AudioSource>();var settings=prefab.GetComponent<ZSFX>();var clips=settings?.m_audioClips;
            if(source==null||clips==null||clips.Length==0||clips[0]==null||clips[0].loadState!=AudioDataLoadState.Loaded)return;
            var group=source.outputAudioMixerGroup;
            if(group==null){var groups=AudioMan.instance?.m_masterMixer?.FindMatchingGroups("SFX");if(groups==null||groups.Length==0)return;group=groups[0];}
            var own=new GameObject("VM_BirthSound");Sounds.Add(own);own.transform.position=transform.position;
            var audio=own.AddComponent<AudioSource>();audio.playOnAwake=false;audio.loop=false;audio.clip=clips[0];audio.outputAudioMixerGroup=group;audio.spatialBlend=1;
            audio.minDistance=2;audio.maxDistance=32;audio.rolloffMode=AudioRolloffMode.Logarithmic;audio.dopplerLevel=0;
            audio.volume=Mathf.Clamp01((settings.m_minVol+settings.m_maxVol)*.5f*gain);audio.pitch=Mathf.Clamp((settings.m_minPitch+settings.m_maxPitch)*.5f,.5f,2);
            float duration=Mathf.Min(.8f,audio.clip.length/audio.pitch);own.AddComponent<PerkAudioLifetime>().Arm(audio,0,duration,.02f,Mathf.Max(0,duration-.12f),.12f);
            audio.Play();SoundUntil=Time.unscaledTime+1;
        }
        private void Queue()
        {
            if(!Context())return;TemplatesCurrent();Data=View.GetZDO();Born=Data.GetLong(MasterIdolForestGuardState.BornKey,0);Nonce=Data.GetString(MasterIdolForestGuardState.NonceKey,"");
            float age=(float)((ZNet.instance.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
            if(Born<=0||age<0||age>Admission){End();return;}if(!Local(true))return;
            string key=Data.m_uid+"/"+Nonce;if(Seen.ContainsKey(key)){End();return;}
            if(Seen.Count>=16){var old=new List<string>();long now=ZNet.instance.GetTime().Ticks;foreach(var item in Seen)if(now-item.Value>Admission*TimeSpan.TicksPerSecond)old.Add(item.Key);foreach(var item in old)Seen.Remove(item);}
            if(Seen.Count>=16||Waiting.Count>=4)return;
            Session=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;Owner=Data.GetOwner();Revision=Data.OwnerRevision;
            InitialHealth=Actor.GetHealth();Origin=transform.position;if(!Peace()){End();return;}
            Seen.Add(key,Born);QueueUntil=Time.unscaledTime+Mathf.Min(6.5f,Admission-age);
            if(!MasteryPlugin.Settings.EnablePerkProcVFX.Value){Tried=true;Warm();Sound(WoodSound,.35f);return;}
            Queued=true;Waiting.Add(this);
        }
        private static string Relative(Transform root,Transform node)
        {
            if(node==root)return "";if(node==null||!node.IsChildOf(root))throw new InvalidOperationException("Foreign cosmetic bone");
            var parts=new List<string>();for(var current=node;current!=root;current=current.parent)parts.Add(current.name);
            parts.Reverse();return string.Join("/",parts);
        }
        private static Transform Locate(Transform root,string path)=>path.Length==0?root:root.Find(path);
        private static void CopyTransform(Transform source,Transform target)
        {target.localPosition=source.localPosition;target.localRotation=source.localRotation;target.localScale=source.localScale;}
        private static void RenderSettings(Renderer renderer)
        {renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;}
        private static GameObject BodyTemplate(string family)
        {
            string key="vfx_VM_BF_BirthBody_"+family;if(Templates.TryGetValue(key,out var cached))return cached;
            var source=ZNetScene.instance?.GetPrefab(family)?.transform.Find("Visual");if(source==null)return null;
            var nodes=source.GetComponentsInChildren<Transform>(true);if(nodes.Length>64)return null;
            var root=new GameObject(key);root.SetActive(false);var visual=new GameObject("Visual").transform;visual.SetParent(root.transform,false);CopyTransform(source,visual);
            var map=new Dictionary<string,Transform>(StringComparer.Ordinal){{"",visual}};
            try
            {
                foreach(var node in nodes)
                {
                    if(node==source)continue;string path=Relative(source,node),parent=Relative(source,node.parent);
                    var target=new GameObject(node.name).transform;target.SetParent(map[parent],false);CopyTransform(node,target);map.Add(path,target);
                }
                foreach(string path in new[]{"Cube","Cube.001"})
                {
                    var original=source.Find(path)?.GetComponent<SkinnedMeshRenderer>();if(original==null||original.sharedMesh==null||original.sharedMaterials.Length!=1)throw new InvalidOperationException("Body resource mismatch");
                    var skin=map[path].gameObject.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=original.sharedMesh;skin.sharedMaterials=original.sharedMaterials;skin.localBounds=original.localBounds;skin.quality=original.quality;skin.updateWhenOffscreen=false;
                    var bones=original.bones;if(bones.Length>48)throw new InvalidOperationException("Bone budget");var own=new Transform[bones.Length];
                    for(int i=0;i<bones.Length;i++)own[i]=map[Relative(source,bones[i])];skin.bones=own;skin.rootBone=map[Relative(source,original.rootBone)];RenderSettings(skin);
                }
                Templates.Add(key,root);return root;
            }
            catch{Destroy(root);return null;}
        }
        private static GameObject FragmentTemplate(string name)
        {
            string key="vfx_VM_BF_Birth_"+name;if(Templates.TryGetValue(key,out var cached))return cached;
            var source=ObjectDB.instance?.GetItemPrefab(name);if(source==null)return null;
            string path=name=="Wood"?"log (1)":name=="Resin"?"model":"attach/model";
            var node=source.transform.Find(path);var filter=node?.GetComponent<MeshFilter>();var renderer=node?.GetComponent<MeshRenderer>();
            if(filter?.sharedMesh==null||renderer==null||renderer.sharedMaterials.Length!=1)return null;
            var root=new GameObject(key);root.SetActive(false);var mesh=new GameObject("mesh");mesh.transform.SetParent(root.transform,false);
            // Preserve native item scale relative to its prefab without carrying item behavior.
            mesh.transform.localPosition=source.transform.InverseTransformPoint(node.position);mesh.transform.localRotation=Quaternion.Inverse(source.transform.rotation)*node.rotation;mesh.transform.localScale=node.lossyScale;
            mesh.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;var own=mesh.AddComponent<MeshRenderer>();own.sharedMaterials=renderer.sharedMaterials;RenderSettings(own);
            Templates.Add(key,root);return root;
        }
        private bool BindBody()
        {
            var original=Actor.m_visual?.transform;var clone=Body?.transform.GetChild(0).Find("Visual");if(original==null||clone==null)return false;
            var nodes=original.GetComponentsInChildren<Transform>(true);if(nodes.Length>64)return false;
            foreach(var node in nodes){var target=Locate(clone,Relative(original,node));if(target==null)return false;CopyTransform(node,target);}
            foreach(string path in new[]{"Cube","Cube.001"})
            {
                var source=original.Find(path)?.GetComponent<SkinnedMeshRenderer>();var copy=clone.Find(path)?.GetComponent<SkinnedMeshRenderer>();if(source==null||copy==null||source.sharedMesh!=copy.sharedMesh)return false;
                var bones=source.bones;if(bones.Length>48)return false;var own=new Transform[bones.Length];
                for(int i=0;i<bones.Length;i++){own[i]=Locate(clone,Relative(original,bones[i]));if(own[i]==null||!own[i].IsChildOf(clone))return false;}
                copy.bones=own;copy.rootBone=Locate(clone,Relative(original,source.rootBone));if(copy.rootBone==null||!copy.rootBone.IsChildOf(clone))return false;
                copy.sharedMaterials=source.sharedMaterials;copy.localBounds=source.localBounds;var block=new MaterialPropertyBlock();source.GetPropertyBlock(block);copy.SetPropertyBlock(block);
            }
            return true;
        }
        private bool Prepared(GameObject obj)=>obj!=null&&obj.activeInHierarchy&&obj.GetComponent<DeferredNativeVfx>()?.IsPrepared==true;
        private GameObject OwnVisual(GameObject template,Quaternion rotation)
        {
            if(template==null||OwnedVisuals.Count>=9)return null;
            var visual=PerkNativeFeedback.CreateVisualOnly(template);if(visual==null)return null;
            // Track before tuning: even partial setup failure has an owned cleanup path.
            OwnedVisuals.Add(visual);visual.transform.SetPositionAndRotation(Origin,rotation);
            Destroy(visual,OwnedLifetime);return visual;
        }
        private static bool PresentationWindow(float elapsed){return float.IsFinite(elapsed)&&elapsed>=0&&elapsed<Duration;}
        private static float Phase(float elapsed,float start,float end){float value=Mathf.Clamp01((elapsed-start)/(end-start));return value*value*(3-2*value);}
        private void Begin()
        {
            if(!Queued||!Context())return;
            float elapsed=(float)((ZNet.instance.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
            if(elapsed<0||elapsed>Admission||Time.unscaledTime>=QueueUntil){End();return;}
            if(!Local(true)||Current!=null||MasterIdolVisibleGesture.GraphicsCount+MasterIdolResidentSceneAnimation.GraphicsCount>=4)return;
            Origin=transform.position;if(!Peace()){End();return;}
            Warm();TemplatesCurrent();if(!Queued)return;var template=BodyTemplate(Utils.GetPrefabName(gameObject));
            if(template==null||!Templates.TryGetValue("vfx_VM_BF_BirthAtmosphere_cloud",out var cloud)||!Templates.TryGetValue("vfx_VM_BF_BirthAtmosphere_dust",out var dust))return;
            Waiting.Remove(this);Queued=false;Current=this;Pending=true;Active=false;StartedAt=0;PrepareUntil=Mathf.Min(QueueUntil,Time.unscaledTime+Preparation);
            try
            {
                Body=OwnVisual(template,transform.rotation);if(Body==null){End(false);return;}Body.transform.localScale=transform.lossyScale*.02f;Body.SetActive(true);
                int family=MasterIdolForestRules.Family(Utils.GetPrefabName(gameObject));
                var names=new List<string>{"AncientSeed","Wood","Resin"};if(family!=3)names.Add("Wood");if(family!=0)names.Add("Stone");if(family==3)names.Add("Resin");if(family==2)names.Add("Stone");
                for(int i=0;i<names.Count;i++)
                {
                    var item=FragmentTemplate(names[i]);if(item==null){End(false);return;}var obj=OwnVisual(item,Quaternion.identity);if(obj==null){End(false);return;}
                    obj.SetActive(true);Fragments.Add(obj);float angle=i*2.399963f;Starts.Add(i==0?Vector3.zero:new Vector3(Mathf.Cos(angle),.15f,Mathf.Sin(angle))*(1.5f+(i%2)*1.5f));
                }
                foreach(var effect in new[]{cloud,dust}){var obj=OwnVisual(effect,Quaternion.identity);if(obj==null){End(false);return;}obj.SetActive(true);Atmosphere.Add(obj);}
                NativeVfxSafeFrame.AfterReady(Body,SuppressReady);foreach(var obj in Fragments)NativeVfxSafeFrame.AfterReady(obj,SuppressReady);
                foreach(var obj in Atmosphere)NativeVfxSafeFrame.AfterReady(obj,()=>{if(Pending)foreach(var ps in obj.GetComponentsInChildren<ParticleSystem>(true))ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);SuppressReady();});
            }
            catch{End(false);}
        }
        private void SuppressReady()
        {
            try
            {
            if(!Pending||Suppressed||!Prepared(Body))return;foreach(var obj in Fragments)if(!Prepared(obj))return;foreach(var obj in Atmosphere)if(!Prepared(obj))return;
            float elapsed=(float)((Session.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
            if(elapsed<0||elapsed>Admission||Time.unscaledTime>=PrepareUntil||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||
                !Context()||!ReferenceEquals(Data,View.GetZDO())||Data.GetOwner()!=Owner||Data.OwnerRevision!=Revision||Data.GetLong(MasterIdolForestGuardState.BornKey,0)!=Born||
                Data.GetString(MasterIdolForestGuardState.NonceKey,"")!=Nonce||!BindBody()||!Peace()||!Local(true)){End();return;}
            var renderers=Actor.m_visual.GetComponentsInChildren<Renderer>(true);if(renderers.Length>6){End();return;}
            foreach(var renderer in renderers){Originals.Add(renderer);OriginalEnabled.Add(renderer.enabled);}
            Suppressed=true;StartedAt=Time.unscaledTime;Tried=true;Pending=false;Active=true;foreach(var renderer in Originals)renderer.enabled=false;
            foreach(var obj in Atmosphere)foreach(var ps in obj.GetComponentsInChildren<ParticleSystem>(true))ps.Play(true);
            StonePlayed=false;Sound(WoodSound,.35f);
            }
            catch{End();}
        }
        private void LateUpdate()
        {
            try
            {
                if(Sounds.Count>0&&!MasteryPlugin.Settings.EnablePerkSFX.Value){foreach(var obj in Sounds)try{if(obj!=null)Destroy(obj);}catch(Exception){}Sounds.Clear();}
                if(Sounds.Count>0&&!Active&&!Pending&&!Queued&&(!Context()||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||Data?.GetOwner()!=Owner||Data?.OwnerRevision!=Revision||Time.unscaledTime>=SoundUntil)){End();return;}
                if(!Tried&&!Pending&&!Active)
                {
                    if(Time.unscaledTime<NextAttempt)return;NextAttempt=Time.unscaledTime+.1f;
                    if(Actor==null||Actor.IsDead()||View?.IsValid()!=true||ZNet.instance?.GetWorld()==null){End();return;}
                    long birth=View.GetZDO().GetLong(MasterIdolForestGuardState.BornKey,0);
                    float age=(float)((ZNet.instance.GetTime().Ticks-birth)/(double)TimeSpan.TicksPerSecond);
                    if(birth<=0||age<0||age>Admission){End();return;}
                    if(!Queued){Queue();if(!Queued)return;}
                    if(Time.unscaledTime>=QueueUntil){End();return;}
                    if(!Context()||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||!ReferenceEquals(Data,View.GetZDO())||Data.GetOwner()!=Owner||Data.OwnerRevision!=Revision||
                        Data.GetLong(MasterIdolForestGuardState.BornKey,0)!=Born||Data.GetString(MasterIdolForestGuardState.NonceKey,"")!=Nonce){End();return;}
                    Origin=transform.position;if(!Peace()||!Local(true)){End();return;}
                    if(Waiting.Count>0&&Waiting[0]==this)Begin();
                    if(Queued)return;
                }
                if(!Active&&!Pending)return;
                float elapsed=(float)((Session.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
                if(!Context()||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||!ReferenceEquals(Data,View.GetZDO())||Data.GetOwner()!=Owner||Data.OwnerRevision!=Revision||
                    Data.GetLong(MasterIdolForestGuardState.BornKey,0)!=Born||Data.GetString(MasterIdolForestGuardState.NonceKey,"")!=Nonce||
                    elapsed<0||!Suppressed&&(elapsed>Admission||Time.unscaledTime>=PrepareUntil)||Suppressed&&!PresentationWindow(Time.unscaledTime-StartedAt)||!Peace()||!Local(false)||!MasteryPlugin.Settings.EnablePerkProcVFX.Value){End();return;}
                if(Time.unscaledTime>=NextSight){NextSight=Time.unscaledTime+.2f;if(!Local(true)){End();return;}}
                if(!Suppressed)
                {
                    SuppressReady();if(!Suppressed)return;
                }
                float visualElapsed=Time.unscaledTime-StartedAt,seed=Phase(visualElapsed,0,1.8f),gather=Phase(visualElapsed,1.8f,6),growth=Phase(visualElapsed,6,Duration);
                if(!StonePlayed&&visualElapsed>=5.5f){StonePlayed=true;Sound(StoneSound,.4f);}
                Body.transform.SetPositionAndRotation(transform.position,transform.rotation);Body.transform.localScale=transform.lossyScale*Mathf.Lerp(.02f,1,growth);
                for(int i=0;i<Fragments.Count;i++)
                {
                    var obj=Fragments[i];if(obj==null){End();return;}Vector3 end=i==0?Vector3.zero:new Vector3(Mathf.Sin(i),.2f+(i%3)*.22f,Mathf.Cos(i))*.3f;
                    obj.transform.position=transform.position+Vector3.Lerp(Starts[i],end,gather);float size=i==0?Mathf.Lerp(.05f,.45f,seed):1f;obj.transform.localScale=Vector3.one*size*(1-growth);
                }
            }
            catch{End();}
        }
        private void End(bool terminal=true)
        {
            Active=false;Pending=false;Queued=false;Waiting.Remove(this);if(terminal)Tried=true;
            try{for(int i=0;i<Originals.Count;i++)try{if(Originals[i]!=null)Originals[i].enabled=OriginalEnabled[i];}catch(Exception){MasterIdolResidentMetrics.Count("birth.restore-failed");}}
            finally
            {
                Originals.Clear();OriginalEnabled.Clear();Suppressed=false;
                if(Current==this)Current=null;
                Body=null;
                foreach(var obj in OwnedVisuals)try{if(obj!=null){try{obj.SetActive(false);}finally{Destroy(obj);}}}catch(Exception){MasterIdolResidentMetrics.Count("birth.owned-cleanup-failed");}
                OwnedVisuals.Clear();Fragments.Clear();Starts.Clear();Atmosphere.Clear();
                foreach(var obj in Sounds)try{if(obj!=null)Destroy(obj);}catch(Exception){MasterIdolResidentMetrics.Count("birth.sound-cleanup-failed");}Sounds.Clear();
                if(!terminal&&!Tried&&Time.unscaledTime<QueueUntil&&Waiting.Count<4){Queued=true;Waiting.Add(this);}
            }
        }
        private void OnDisable()=>End();private void OnDestroy()=>End();
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolForestBirthAttachPatch
    {private static void Postfix(BaseAI __instance)=>MasterIdolResidentBirthPresentation.Attach(__instance.GetComponent<Character>());}
    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class MasterIdolBirthSceneWarmPatch{private static void Postfix()=>MasterIdolResidentBirthPresentation.Warm();}
    [HarmonyPatch(typeof(ObjectDB),"Awake")]
    internal static class MasterIdolBirthDatabaseWarmPatch{private static void Postfix()=>MasterIdolResidentBirthPresentation.Warm();}
    [HarmonyPatch(typeof(ObjectDB),nameof(ObjectDB.CopyOtherDB))]
    internal static class MasterIdolBirthDatabaseCopyWarmPatch{private static void Postfix()=>MasterIdolResidentBirthPresentation.Warm();}
    [HarmonyPatch(typeof(ZNetScene),"OnDestroy")]
    internal static class MasterIdolBirthSceneCleanupPatch{private static void Postfix()=>MasterIdolResidentBirthPresentation.ClearOwned();}
    [HarmonyPatch(typeof(MasterIdolLoadIdentity),nameof(MasterIdolLoadIdentity.Reset))]
    internal static class MasterIdolBirthGenerationCleanupPatch{private static void Postfix()=>MasterIdolResidentBirthPresentation.ClearOwned();}
}
