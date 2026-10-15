using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Only owned local particles. Shared native materials and their bindings stay untouched.
    internal static class WorkbenchEnchantmentThematicBurst
    {
        internal const float Lifetime=7f,LearnLifetime=5f;
        internal const int MaxParticles=96,LocalCount=24,AnchorCount=12,MaxAnchors=5,MaxSamples=32;
        internal static float Duration(bool firstLearn)=>firstLearn?LearnLifetime:Lifetime;
        internal static float FormationProgress(float elapsed)=>Mathf.Clamp01(elapsed/5.2f);
        internal static int Waves(bool firstLearn)=>firstLearn?6:10;
        internal const string SmokePrefab="Assets/Characters/Ghost/Ghost_sleeping.prefab";
        internal static float LocalAt(int index,bool firstLearn=false)
        {
            if(!firstLearn)return index>=0&&index<10?Mathf.Min(5.2f,.1f+index*.6f):float.PositiveInfinity;
            switch(index){case 0:return 0f;case 1:return .8f;case 2:return 1.6f;case 3:return 2.4f;case 4:return 3.2f;case 5:return 4.2f;default:return float.PositiveInfinity;}
        }
        internal static bool Timely(float elapsed,float scheduled,bool firstLearn=false)=>elapsed>=scheduled&&elapsed-scheduled<=.2f&&elapsed<Duration(firstLearn);
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
            var stars=firstLearn?null:NativeSoftVisualAssets.Get<Material>(MaterialPath(type,true));if(!firstLearn&&stars==null)return null;
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
                main.playOnAwake=false;main.loop=false;main.duration=Duration(firstLearn);main.maxParticles=firstLearn?MaxParticles:32;
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
                if(!firstLearn)
                {
                    var station=bench.GetComponent<CraftingStation>();
                    float radius=station==null?0:WorkshopStationRadius.EffectiveRadius(station.m_rangeBuild,station.GetLevel(false));
                    if(!float.IsFinite(radius)||radius<=0||radius>128){UnityEngine.Object.Destroy(root);return null;}
                    var rig=root.AddComponent<WorkbenchEnchantmentAreaRig>();rig.Fog=particles;rig.Radius=radius;
                    var child=new GameObject("VM_EnchantmentMeaning");child.layer=root.layer;child.transform.SetParent(root.transform,false);
                    var sparks=child.AddComponent<ParticleSystem>();sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                    var sparkMain=sparks.main;sparkMain.playOnAwake=false;sparkMain.loop=false;sparkMain.duration=Lifetime;sparkMain.maxParticles=64;
                    sparkMain.simulationSpace=ParticleSystemSimulationSpace.World;sparkMain.useUnscaledTime=true;sparkMain.startSpeed=0;
                    var sparkEmission=sparks.emission;sparkEmission.enabled=false;var sparkShape=sparks.shape;sparkShape.enabled=false;
                    var sparkFade=sparks.colorOverLifetime;sparkFade.enabled=true;sparkFade.color=gradient;
                    var sparkRenderer=sparks.GetComponent<ParticleSystemRenderer>();sparkRenderer.sharedMaterial=stars;
                    sparkRenderer.renderMode=ParticleSystemRenderMode.Billboard;sparkRenderer.shadowCastingMode=ShadowCastingMode.Off;
                    sparkRenderer.receiveShadows=false;sparkRenderer.lightProbeUsage=LightProbeUsage.Off;sparkRenderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                    rig.Sparks=sparks;
                }
                root.SetActive(true);particles.gameObject.SetActive(true);particles.Play();
                root.GetComponent<WorkbenchEnchantmentAreaRig>()?.Sparks.Play();
                return root;
            }
            catch(Exception){if(root!=null)UnityEngine.Object.Destroy(root);return null;}
        }
        internal static void Emit(GameObject root,string type,Vector3 anchor,Color color,int count,bool firstLearn,float progress=0)
        {
            if(root==null||!Finite(anchor)||count<1)return;
            var particles=root.GetComponentInChildren<ParticleSystem>(true);if(particles==null)return;
            count=Math.Min(LocalCount,count);
            if(!firstLearn){EmitArea(root.GetComponent<WorkbenchEnchantmentAreaRig>(),type,anchor,color,count,progress);return;}
                for(int i=0;i<count;i++)
                {
                    float angle=i*2.399963f;float radius=.12f+.14f*(i%4)/3f;
                    float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
                    Vector3 position=new Vector3(x*radius,0,z*radius);
                    Vector3 velocity=new Vector3(-z*.18f,.18f,x*.18f);float size=.045f;
                    var tint=color;tint.a=.85f;
                    particles.Emit(new ParticleSystem.EmitParams{position=anchor+position,velocity=velocity,startSize=size,
                        startLifetime=.6f+.2f*(i%3)/2f,startColor=tint},1);
                }
        }
        internal readonly struct AreaPoint
        {
            internal readonly Vector3 Position,Velocity;internal readonly Color Color;internal readonly float Size;
            internal AreaPoint(Vector3 position,Vector3 velocity,Color color,float size){Position=position;Velocity=velocity;Color=color;Size=size;}
        }
        // Symbolic growth/assembly/shelter/repair/renewal/air, independent of world objects.
        internal static AreaPoint Sample(string type,float progress,int index,float radius,bool fog)
        {
            float p=Mathf.Clamp01(progress),angle=index*2.399963f,x=Mathf.Cos(angle),z=Mathf.Sin(angle);
            float distance=radius*(.15f+.75f*p),height=.15f;var velocity=Vector3.up*.3f;
            var color=new Color(1,.8f,.28f,1);float size=Mathf.Clamp(radius*(fog?.07f:.022f),fog?1.1f:.35f,fog?3.2f:1f);
            var position=new Vector3(x*distance,height,z*distance);
            switch(type)
            {
                case "Meadows":
                    float patch=(index/3)*2.399963f;float stem=Mathf.Min(1,p*1.6f);
                    position=new Vector3(Mathf.Cos(patch)*radius*.6f,(index%3==0?1:1.6f)*stem,Mathf.Sin(patch)*radius*.6f);
                    if(index%3!=0)position+=new Vector3((index%3==1?-1:1)*stem*.45f,0,0);
                    velocity=new Vector3(0,.6f,0);color=index%3==0?new Color(.4f,1,.3f,1):new Color(1,.88f,.35f,1);break;
                case "BlackForest":
                    float core=(index%4)*Mathf.PI*.5f;var center=new Vector3(Mathf.Cos(core),0,Mathf.Sin(core))*radius*.45f;
                    var inward=new Vector3(x,0,z)*(radius*.25f*(1-p));position=center+inward+Vector3.up*(.15f+p*1.1f);
                    velocity=-new Vector3(x,0,z)*Mathf.Min(.6f,radius*.25f*(1-p)/(fog?1.6f:1.2f))+Vector3.up*.1f;color=new Color(.48f,.86f,.32f,1);break;
                case "Swamp":
                    float latitude=(index%5)*Mathf.PI/10;float opening=.12f+.88f*p;
                    position=new Vector3(x*Mathf.Cos(latitude),Mathf.Sin(latitude),z*Mathf.Cos(latitude))*radius*opening;
                    velocity=new Vector3(x*.45f,.05f,z*.45f);color=new Color(.55f,.8f,.76f,1);break;
                case "Mountain":
                    if((index&1)==0){position=new Vector3(x*radius*.65f,.15f+(index%3)*.2f,z*radius*.65f);velocity=Vector3.up*.9f;color=new Color(1,.55f,.17f,1);}
                    else{int side=(index/2)%4;float along=radius*.65f*(1-p),speed=Mathf.Min(.7f,along/(fog?1.6f:1.2f));position=side<2?new Vector3((side==0?-1:1)*along,.6f,(index%3-1)*radius*.35f):new Vector3((index%3-1)*radius*.35f,.6f,(side==2?-1:1)*along);velocity=side<2?new Vector3(side==0?speed:-speed,0,0):new Vector3(0,0,side==2?speed:-speed);color=new Color(.72f,.9f,1,1);}break;
                case "Plains":
                    position=new Vector3(x*radius*(.3f+.5f*p),.25f+p*.8f,z*radius*(.3f+.5f*p));
                    velocity=new Vector3(-z*.12f,.22f,x*.12f);color=new Color(1,.8f,.35f,1);break;
                case "Mistlands":
                    float turn=angle+p*Mathf.PI;position=new Vector3(Mathf.Cos(turn)*distance,.35f+p,Mathf.Sin(turn)*distance);
                    velocity=new Vector3(-Mathf.Sin(turn)*.3f,.22f,Mathf.Cos(turn)*.3f);color=new Color(.72f,.5f,1,1);break;
            }
            color.a=fog?.3f:.9f;return new AreaPoint(position,velocity,color,size);
        }
        private static void EmitArea(WorkbenchEnchantmentAreaRig rig,string type,Vector3 anchor,Color color,int count,float progress)
        {
            if(rig?.Fog==null||rig.Sparks==null||!float.IsFinite(progress))return;
            var origin=anchor-new Vector3(0,1.0f,.18f);int fogCount=count/3;
            for(int i=0;i<count;i++)
            {
                bool fog=i<fogCount;var point=Sample(type,progress,fog?i:i-fogCount,rig.Radius,fog);
                (fog?rig.Fog:rig.Sparks).Emit(new ParticleSystem.EmitParams{position=origin+point.Position,velocity=point.Velocity,
                    startSize=point.Size,startLifetime=fog?1.6f:1.2f,startColor=point.Color},1);
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
            var rig=root.GetComponent<WorkbenchEnchantmentAreaRig>();var sparkShader=rig?.Sparks?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.shader;
            return "active="+root.activeInHierarchy+" renderer="+(renderer?.enabled==true)+" particles="+(ps?.particleCount??0)+
                " material="+(material?.name??"none")+" shader="+(shader?.name??"none")+" supported="+(shader?.isSupported==true)+" texture="+(texture?.name??"none")+" streams="+string.Join(",",streams)+
                " areaRadius="+(rig?.Radius??0)+" stars="+(rig?.Sparks?.particleCount??0)+" starShader="+(sparkShader?.name??"none")+" starSupported="+(sparkShader?.isSupported==true);
        }
        static bool Finite(Vector3 point)=>!float.IsNaN(point.x)&&!float.IsInfinity(point.x)&&!float.IsNaN(point.y)&&!float.IsInfinity(point.y)&&!float.IsNaN(point.z)&&!float.IsInfinity(point.z);
    }
    internal sealed class WorkbenchEnchantmentAreaRig:MonoBehaviour
    {internal ParticleSystem Fog,Sparks;internal float Radius;}
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
