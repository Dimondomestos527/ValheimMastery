using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Only owned local particles. Shared native materials and their bindings stay untouched.
    internal static class WorkbenchEnchantmentThematicBurst
    {
        internal const float Lifetime=5f;
        internal const int MaxParticles=96,LocalCount=24,AnchorCount=12,MaxAnchors=5,MaxSamples=32;
        internal const string SmokePrefab="Assets/Characters/Ghost/Ghost_sleeping.prefab";
        internal static float LocalAt(int index)
        {switch(index){case 0:return 0f;case 1:return .8f;case 2:return 1.6f;case 3:return 2.4f;case 4:return 3.2f;case 5:return 4.2f;default:return float.PositiveInfinity;}}
        internal static bool Timely(float elapsed,float scheduled)=>elapsed>=scheduled&&elapsed-scheduled<=.2f&&elapsed<Lifetime;
        static ZNetScene preparedScene;static bool preparedVfx,preparedSfx;
        static readonly string[] Types={"Meadows","BlackForest","Swamp","Mountain","Plains","Mistlands"};
        internal static string MaterialPath(string type,bool firstLearn)
        {
            switch(type)
            {
                // Learn: the selected original Forest stars, tinted for each theme.
                // Activate: the selected soft Mistlands fog and thematic drift.
                case "Meadows":case "BlackForest":case "Swamp":case "Mountain":case "Plains":
                case "Mistlands":return firstLearn?"Assets/Effects/materials/Star.mat":"Assets/Effects/materials/slowwispysmoke_gradient_alphablend.mat";
                default:return null;
            }
        }
        internal static void Prepare()
        {
            if(Application.isBatchMode||ZNetScene.instance==null)return;
            bool vfx=MasteryPlugin.Settings.EnablePerkProcVFX.Value,sfx=MasteryPlugin.Settings.EnablePerkSFX.Value;
            if(sfx)EnchantmentRitualAudio.Preload();
            if(preparedScene==ZNetScene.instance&&preparedVfx==vfx&&preparedSfx==sfx)return;
            preparedScene=ZNetScene.instance;preparedVfx=vfx;preparedSfx=sfx;
            if(vfx)foreach(string type in Types)
            {NativeSoftVisualAssets.Get<Material>(MaterialPath(type,true));NativeSoftVisualAssets.Get<Material>(MaterialPath(type,false));}
            if(vfx)NativeSoftVisualAssets.Get<GameObject>(SmokePrefab);
        }
        internal static GameObject Create(Transform bench,string type,bool firstLearn,Color color)
        {
            if(bench==null||Application.isBatchMode||!MasteryPlugin.Settings.EnablePerkProcVFX.Value)return null;
            string path=MaterialPath(type,firstLearn);if(path==null)return null;
            var material=NativeSoftVisualAssets.Get<Material>(path);if(material==null)return null;
            Transform donor=null;
            if(!firstLearn)
            {
                donor=NativeSoftVisualAssets.Get<GameObject>(SmokePrefab)?.transform.Find("Visual/black_smoke");
                // Read only this exact native emitter. No donor object or children are cloned.
                if(donor==null||donor.GetComponent<ParticleSystem>()==null||
                    donor.GetComponent<ParticleSystemRenderer>()?.sharedMaterial!=material)return null;
            }
            GameObject root=null;
            try
            {
                root=new GameObject("VM_EnchantmentBurst_"+type);root.SetActive(false);root.layer=bench.gameObject.layer;
                root.transform.SetParent(bench,false);root.transform.localPosition=new Vector3(0,1.15f,.18f);
                var particles=root.AddComponent<ParticleSystem>();
                particles.gameObject.layer=root.layer;particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=particles.main;
                main.playOnAwake=false;main.loop=false;main.duration=Lifetime;main.maxParticles=MaxParticles;
                main.simulationSpace=ParticleSystemSimulationSpace.World;main.useUnscaledTime=true;
                main.startSpeed=0;main.startLifetime=.8f;particles.useAutoRandomSeed=false;particles.randomSeed=17;
                var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
                var renderer=particles.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
                if(!firstLearn)
                {
                    var native=donor.GetComponent<ParticleSystem>();var source=donor.GetComponent<ParticleSystemRenderer>();
                    var streams=new System.Collections.Generic.List<ParticleSystemVertexStream>();source.GetActiveVertexStreams(streams);renderer.SetActiveVertexStreams(streams);
                    NativeSnowVisualAssets.CopySheet(native,particles);
                    var data=particles.customData;var nativeData=native.customData;data.enabled=nativeData.enabled;
                    data.SetMode(ParticleSystemCustomData.Custom1,nativeData.GetMode(ParticleSystemCustomData.Custom1));
                    data.SetMode(ParticleSystemCustomData.Custom2,nativeData.GetMode(ParticleSystemCustomData.Custom2));
                    data.SetColor(ParticleSystemCustomData.Custom1,nativeData.GetColor(ParticleSystemCustomData.Custom1));
                    data.SetColor(ParticleSystemCustomData.Custom2,nativeData.GetColor(ParticleSystemCustomData.Custom2));
                    // Custom1 is the inspected native gradient color, owned here and themed per biome.
                    data.SetColor(ParticleSystemCustomData.Custom1,new ParticleSystem.MinMaxGradient(new Color(color.r*.35f,color.g*.35f,color.b*.35f,1f)));
                }
                renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                var fade=particles.colorOverLifetime;fade.enabled=true;
                var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(0,1)});fade.color=gradient;
                root.SetActive(true);particles.gameObject.SetActive(true);particles.Play();
                return root;
            }
            catch(Exception){if(root!=null)UnityEngine.Object.Destroy(root);return null;}
        }
        internal static void Emit(GameObject root,string type,Vector3 anchor,Color color,int count,bool firstLearn)
        {
            if(root==null||!Finite(anchor)||count<1)return;
            var particles=root.GetComponentInChildren<ParticleSystem>(true);if(particles==null)return;
            count=Math.Min(LocalCount,count);
                for(int i=0;i<count;i++)
                {
                    float angle=i*2.399963f;float radius=firstLearn?.12f+.14f*(i%4)/3f:.3f+.6f*(i%4)/3f;
                    float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
                    Vector3 position=new Vector3(x*radius,0,z*radius);
                    Vector3 velocity=new Vector3(-z*.18f,.18f,x*.18f);float size=.045f;
                    if(!firstLearn)
                    {
                    velocity=new Vector3(x*.12f,.22f,z*.12f);size=.85f;
                    switch(type)
                    {
                        case "BlackForest":velocity=new Vector3(-z*.12f,.12f,x*.12f);break;
                        case "Swamp":velocity=new Vector3(x*.1f,.1f,z*.1f);size=1f;break;
                        case "Mountain":position.y=.28f;velocity=new Vector3(x*.08f,-.1f,z*.08f);break;
                        case "Plains":velocity=new Vector3(x*.12f,.16f+(i%3)*.03f,z*.12f);break;
                        case "Mistlands":velocity=new Vector3(-z*.16f,.16f,x*.16f);size=1f;break;
                    }
                    }
                    var tint=color;tint.a=firstLearn?.85f:.55f;
                    particles.Emit(new ParticleSystem.EmitParams{position=anchor+position,velocity=velocity,startSize=size,
                        startLifetime=firstLearn?.6f+.2f*(i%3)/2f:1.4f+.3f*(i%3)/2f,startColor=tint},1);
                }
        }
        internal static Vector3[] SampleAnchors(MasterIdolEffectCircle[] circles,Vector3 source,Vector3 viewer,out int count)
        {
            count=0;var result=new Vector3[MaxAnchors];if(circles==null||!Finite(viewer)||!Finite(source))return result;
            int samples=Math.Min(MaxSamples,circles.Length);
            for(int i=0;i<samples&&count<MaxAnchors;i++)
            {
                int at=samples==1?0:(int)((long)i*(circles.Length-1)/(samples-1));var point=circles[at].Point;
                if(!Finite(point)||(point-viewer).sqrMagnitude>576f||(point-source).sqrMagnitude<.36f)continue;
                bool duplicate=false;for(int j=0;j<count;j++)if((result[j]-point).sqrMagnitude<.36f){duplicate=true;break;}
                if(!duplicate)result[count++]=point;
            }
            return result;
        }
        internal static string Describe(GameObject root)
        {
            if(root==null)return "root=none";
            var ps=root.GetComponentInChildren<ParticleSystem>(true);var renderer=ps?.GetComponent<ParticleSystemRenderer>();var material=renderer?.sharedMaterial;
            var shader=material?.shader;var texture=material!=null&&material.HasProperty("_MainTex")?material.GetTexture("_MainTex"):null;
            var streams=new System.Collections.Generic.List<ParticleSystemVertexStream>();if(renderer!=null)renderer.GetActiveVertexStreams(streams);
            return "active="+root.activeInHierarchy+" renderer="+(renderer?.enabled==true)+" particles="+(ps?.particleCount??0)+
                " material="+(material?.name??"none")+" shader="+(shader?.name??"none")+" supported="+(shader?.isSupported==true)+" texture="+(texture?.name??"none")+" streams="+string.Join(",",streams);
        }
        static bool Finite(Vector3 point)=>!float.IsNaN(point.x)&&!float.IsInfinity(point.x)&&!float.IsNaN(point.y)&&!float.IsInfinity(point.y)&&!float.IsNaN(point.z)&&!float.IsInfinity(point.z);
    }
    internal static class WorkbenchEnchantmentVisualTrace
    {
        private static ZNet Session;private static long World;private static int Count;
        internal static void Write(string stage,string type,string detail)
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(Session!=net||World!=world){Session=net;World=world;Count=0;}
            if(Count>=64)return;Count++;
            try{MasteryPlugin.Log?.LogInfo("[EnchantVFX] stage="+stage+" type="+type+" "+detail);}catch(Exception){}
        }
    }
}
