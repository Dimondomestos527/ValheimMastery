using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Only owned local particles. Shared native materials and their bindings stay untouched.
    internal static class WorkbenchEnchantmentThematicBurst
    {
        internal const float Lifetime=1.05f;
        internal const string Sound="sfx_Potion_eitr_minor";
        static ZNetScene preparedScene;static bool preparedVfx,preparedSfx;
        static readonly string[] Types={"Meadows","BlackForest","Swamp","Mountain","Plains","Mistlands"};
        internal static string MaterialPath(string type)
        {
            switch(type)
            {
                case "Meadows":return "Assets/Effects/materials/light_glow.mat";
                case "BlackForest":return "Assets/Effects/materials/Star.mat";
                case "Swamp":return "Assets/Effects/materials/swamp_mist.mat";
                case "Mountain":return "Assets/Effects/materials/snow_flake.mat";
                case "Plains":return "Assets/Effects/materials/FaderEmber_sparks.mat";
                case "Mistlands":return "Assets/Effects/materials/slowwispysmoke_gradient_alphablend.mat";
                default:return null;
            }
        }
        internal static void Prepare()
        {
            if(Application.isBatchMode||ZNetScene.instance==null)return;
            bool vfx=MasteryPlugin.Settings.EnablePerkProcVFX.Value,sfx=MasteryPlugin.Settings.EnablePerkSFX.Value;
            if(preparedScene==ZNetScene.instance&&preparedVfx==vfx&&preparedSfx==sfx)return;
            preparedScene=ZNetScene.instance;preparedVfx=vfx;preparedSfx=sfx;
            if(vfx)foreach(string type in Types)NativeSoftVisualAssets.Get<Material>(MaterialPath(type));
            if(sfx)NativePerkAssetResolver.Resolve(Sound);
        }
        internal static GameObject Create(Transform bench,string type,bool firstLearn,Color color)
        {
            if(bench==null||Application.isBatchMode||!MasteryPlugin.Settings.EnablePerkProcVFX.Value)return null;
            string path=MaterialPath(type);if(path==null)return null;
            var material=NativeSoftVisualAssets.Get<Material>(path);if(material==null)return null;
            GameObject root=null;
            try
            {
                root=new GameObject("VM_EnchantmentBurst_"+type);root.layer=bench.gameObject.layer;
                root.transform.SetParent(bench,false);root.transform.localPosition=new Vector3(0,1.15f,.18f);
                var particles=root.AddComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=particles.main;
                main.playOnAwake=false;main.loop=false;main.duration=.05f;main.maxParticles=24;
                main.simulationSpace=ParticleSystemSimulationSpace.Local;main.useUnscaledTime=true;
                main.startSpeed=0;main.startLifetime=.85f;particles.useAutoRandomSeed=false;particles.randomSeed=17;
                var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
                var renderer=root.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
                renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=ShadowCastingMode.Off;
                renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                var fade=particles.colorOverLifetime;fade.enabled=true;
                var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                    new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(0,1)});fade.color=gradient;
                int count=firstLearn?24:16;bool mist=type=="Swamp"||type=="Mistlands";
                particles.Play();
                for(int i=0;i<count;i++)
                {
                    float angle=i*2.399963f;float radius=.12f+.14f*(i%4)/3f;
                    float x=Mathf.Cos(angle),z=Mathf.Sin(angle);
                    Vector3 position=new Vector3(x*radius,0,z*radius);
                    Vector3 velocity=new Vector3(x*.12f,.3f,z*.12f);float size=.035f;
                    switch(type)
                    {
                        case "BlackForest":velocity=new Vector3(-z*.18f,.18f,x*.18f);size=.045f;break;
                        case "Swamp":velocity=new Vector3(x*.1f,.1f,z*.1f);size=.18f;break;
                        case "Mountain":position.y=.28f;velocity=new Vector3(x*.08f,-.22f,z*.08f);size=.045f;break;
                        case "Plains":velocity=new Vector3(x*.3f,.35f+(i%3)*.08f,z*.3f);size=.025f;break;
                        case "Mistlands":velocity=new Vector3(-z*.16f,.16f,x*.16f);size=.16f;break;
                    }
                    var tint=color;tint.a=mist ? .35f : .85f;
                    particles.Emit(new ParticleSystem.EmitParams{position=position,velocity=velocity,startSize=size,
                        startLifetime=.65f+.2f*(i%3)/2f,startColor=tint},1);
                }
                return root;
            }
            catch(Exception){if(root!=null)UnityEngine.Object.Destroy(root);return null;}
        }
    }
}
