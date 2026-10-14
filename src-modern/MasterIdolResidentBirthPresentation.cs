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
        private const float Duration=1.6f;
        private static readonly Dictionary<string,GameObject> Templates=new Dictionary<string,GameObject>(StringComparer.Ordinal);
        private static ZNetScene TemplateScene;
        private static MasterIdolResidentBirthPresentation Current;
        internal static int GraphicsCount=>Current!=null&&Current.Active?1:0;
        internal static bool Playing(Character actor)=>actor?.GetComponent<MasterIdolResidentBirthPresentation>()?.Active==true;
        private Character Actor;private ZNetView View;private ZDO Data;private BaseAI Ai;private bool Tried,Active,Suppressed;
        private ZNet Session;private ZDOMan Manager;private ulong Generation;private long Owner,Born;private ushort Revision;
        private float InitialHealth,NextSight;private Vector3 Origin;private GameObject Body;
        private readonly List<GameObject> Fragments=new List<GameObject>(6);
        private readonly List<Vector3> Starts=new List<Vector3>(6);
        private readonly List<Renderer> Originals=new List<Renderer>(6);
        private readonly List<bool> OriginalEnabled=new List<bool>(6);
        private void Awake(){Actor=GetComponent<Character>();View=GetComponent<ZNetView>();Ai=GetComponent<BaseAI>();}
        internal static void Attach(Character actor)
        {
            if(actor!=null&&MasterIdolForestGuardState.ModuleBorn(actor.m_nview?.GetZDO())&&actor.GetComponent<MasterIdolResidentBirthPresentation>()==null)
                actor.gameObject.AddComponent<MasterIdolResidentBirthPresentation>();
        }
        internal static void Stop(Character actor)=>actor?.GetComponent<MasterIdolResidentBirthPresentation>()?.End();
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
                !MasteryPlugin.Settings.EnablePerkProcVFX.Value||player==null||player.IsDead()||player.IsTeleporting()||player.InGhostMode()||player.IsDebugFlying()||
                camera==null||!camera.isActiveAndEnabled||!player.TakeInput()||InventoryGui.IsVisible()||Game.IsPaused()||Hud.InRadial()||Hud.InBuildUi()||
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
            if(TemplateScene==ZNetScene.instance)return;
            foreach(var template in Templates.Values)if(template!=null)Destroy(template);
            Templates.Clear();TemplateScene=ZNetScene.instance;
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
        private void Begin()
        {
            Tried=true;if(!Context()||!Local(true)||Current!=null||MasterIdolVisibleGesture.GraphicsCount+MasterIdolResidentSceneAnimation.GraphicsCount>=4)return;
            Data=View.GetZDO();Born=Data.GetLong(MasterIdolForestGuardState.BornKey,0);float elapsed=(float)((ZNet.instance.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
            if(elapsed<0||elapsed>.2f)return;InitialHealth=Actor.GetHealth();Origin=transform.position;if(!Peace())return;
            TemplatesCurrent();var template=BodyTemplate(Utils.GetPrefabName(gameObject));if(template==null)return;
            Session=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;Owner=Data.GetOwner();Revision=Data.OwnerRevision;
            Current=this;Active=true;
            try
            {
                Body=VfxPool.Spawn(template,Origin,transform.rotation,1.8f);if(Body==null){End();return;}Body.transform.localScale=transform.lossyScale*.02f;Body.SetActive(true);
                int family=MasterIdolForestRules.Family(Utils.GetPrefabName(gameObject));
                var names=new List<string>{"AncientSeed","Wood","Resin"};if(family!=3)names.Add("Wood");if(family!=0)names.Add("Stone");if(family==3)names.Add("Resin");if(family==2)names.Add("Stone");
                for(int i=0;i<names.Count;i++)
                {
                    var item=FragmentTemplate(names[i]);if(item==null){End();return;}var obj=VfxPool.Spawn(item,Origin,Quaternion.identity,1.8f);if(obj==null){End();return;}
                    obj.SetActive(true);Fragments.Add(obj);float angle=i*2.399963f;Starts.Add(i==0?Vector3.zero:new Vector3(Mathf.Cos(angle),.15f,Mathf.Sin(angle))*(1.5f+(i%2)*1.5f));
                }
                NativeVfxSafeFrame.AfterReady(Body,SuppressReady);foreach(var obj in Fragments)NativeVfxSafeFrame.AfterReady(obj,SuppressReady);
            }
            catch{End();}
        }
        private void SuppressReady()
        {
            try
            {
            if(!Active||Suppressed||!Prepared(Body))return;foreach(var obj in Fragments)if(!Prepared(obj))return;
            float elapsed=(float)((Session.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
            if(elapsed<0||elapsed>.25f||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||
                !Context()||!ReferenceEquals(Data,View.GetZDO())||Data.GetOwner()!=Owner||Data.OwnerRevision!=Revision||!BindBody()||!Peace()||!Local(true)){End();return;}
            var renderers=Actor.m_visual.GetComponentsInChildren<Renderer>(true);if(renderers.Length>6){End();return;}
            foreach(var renderer in renderers){Originals.Add(renderer);OriginalEnabled.Add(renderer.enabled);}
            Suppressed=true;foreach(var renderer in Originals)renderer.enabled=false;
            }
            catch{End();}
        }
        private void LateUpdate()
        {
            try
            {
                if(!Tried){if(Context())Begin();else return;}if(!Active)return;
                float elapsed=(float)((Session.GetTime().Ticks-Born)/(double)TimeSpan.TicksPerSecond);
                if(!Context()||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||!ReferenceEquals(Data,View.GetZDO())||Data.GetOwner()!=Owner||Data.OwnerRevision!=Revision||
                    !MasterIdolForestRules.Birth(elapsed)||!Peace()||!Local(false)){End();return;}
                if(Time.unscaledTime>=NextSight){NextSight=Time.unscaledTime+.2f;if(!Local(true)){End();return;}}
                if(!Suppressed)
                {
                    SuppressReady();if(!Suppressed)return;
                }
                float progress=Mathf.Clamp01(elapsed/Duration),smooth=progress*progress*(3-2*progress);
                Body.transform.SetPositionAndRotation(transform.position,transform.rotation);Body.transform.localScale=transform.lossyScale*Mathf.Lerp(.02f,1,smooth);
                for(int i=0;i<Fragments.Count;i++)
                {
                    var obj=Fragments[i];if(obj==null){End();return;}Vector3 end=i==0?Vector3.zero:new Vector3(Mathf.Sin(i),.2f+(i%3)*.22f,Mathf.Cos(i))*.3f;
                    obj.transform.position=transform.position+Vector3.Lerp(Starts[i],end,smooth);float size=i==0?.45f:1f;obj.transform.localScale=Vector3.one*size*(progress>.8f?Mathf.Clamp01((1-progress)/.2f):1f);
                }
            }
            catch{End();}
        }
        private void End()
        {
            Active=false;
            try{for(int i=0;i<Originals.Count;i++)try{if(Originals[i]!=null)Originals[i].enabled=OriginalEnabled[i];}catch(Exception){MasterIdolResidentMetrics.Count("birth.restore-failed");}}
            finally
            {
                Originals.Clear();OriginalEnabled.Clear();Suppressed=false;
                if(Current==this)Current=null;
                var body=Body;Body=null;
                try{if(body!=null&&body.activeSelf)VfxPool.Return(body.name,body);}catch(Exception){MasterIdolResidentMetrics.Count("birth.pool-return-failed");}
                foreach(var obj in Fragments)try{if(obj!=null&&obj.activeSelf)VfxPool.Return(obj.name,obj);}catch(Exception){MasterIdolResidentMetrics.Count("birth.pool-return-failed");}
                Fragments.Clear();Starts.Clear();
            }
        }
        private void OnDisable()=>End();private void OnDestroy()=>End();
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolForestBirthAttachPatch
    {private static void Postfix(BaseAI __instance)=>MasterIdolResidentBirthPresentation.Attach(__instance.GetComponent<Character>());}
}
