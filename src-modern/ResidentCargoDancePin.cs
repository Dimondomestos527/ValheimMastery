using System;
namespace ValheimMastery
{
    internal static partial class MasterIdolResidentCargo
    {
        // Callback boundary only: never refresh a changed identity into authority.
        // Includes cargo intent fields so optional cleanup cannot create/replace a lease.
        internal readonly struct DancePin
        {
            private readonly Character Actor;
            private readonly ZNetView View;
            private readonly ZDO Data;
            private readonly ZNet Net;
            private readonly ZDOMan DataManager;
            private readonly ZNetScene Scene;
            private readonly ulong LoadGeneration;
            private readonly long WorldId,ActorOwner,Born,OpeningUntil;
            private readonly ushort Revision;
            private readonly string BirthNonce,Home,Epoch,OpenNonce;
            private readonly ZDOID Cargo;
            internal DancePin(Character actor)
            {
                Actor=actor;View=actor?.m_nview;Data=View?.GetZDO();Net=Session;DataManager=Manager;Scene=ZNetScene.instance;
                LoadGeneration=Generation;WorldId=World;ActorOwner=Data?.GetOwner()??0;Revision=Data?.OwnerRevision??0;
                Born=Data?.GetLong(MasterIdolForestGuardState.BornKey,0)??0;BirthNonce=Data?.GetString(MasterIdolForestGuardState.NonceKey,"");
                Home=Data?.GetString(MasterIdolNativeTaming.HomeKey,"");Cargo=Data?.GetZDOID(Link)??ZDOID.None;
                Epoch=Data?.GetString(EpochKey,"");OpeningUntil=Data?.GetLong(OpeningKey,0)??0;OpenNonce=Data?.GetString(OpeningNonce,"");
            }
            internal bool Current()
                =>Actor!=null&&View!=null&&Data!=null&&Net!=null&&DataManager!=null&&Scene!=null&&
                ReferenceEquals(Net,Session)&&ReferenceEquals(Net,ZNet.instance)&&ReferenceEquals(DataManager,Manager)&&ReferenceEquals(DataManager,ZDOMan.instance)&&
                ReferenceEquals(Scene,ZNetScene.instance)&&WorldId!=0&&WorldId==World&&Net.GetWorld()!=null&&Net.GetWorldUID()==WorldId&&
                LoadGeneration==Generation&&LoadGeneration==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady&&
                ReferenceEquals(Actor.m_nview,View)&&View.IsValid()&&View.IsOwner()&&ReferenceEquals(View.GetZDO(),Data)&&
                ReferenceEquals(DataManager.GetZDO(Data.m_uid),Data)&&ReferenceEquals(ActorFor(Data),Actor)&&MasterIdolForestGuardState.ModuleBorn(Data)&&
                !Actor.IsDead()&&Data.GetOwner()==ActorOwner&&ActorOwner==ZNet.GetUID()&&Data.OwnerRevision==Revision&&
                Data.GetLong(MasterIdolForestGuardState.BornKey,0)==Born&&Data.GetString(MasterIdolForestGuardState.NonceKey,"")==BirthNonce&&
                Data.GetString(MasterIdolNativeTaming.HomeKey,"")==Home&&Data.GetZDOID(Link)==Cargo&&Data.GetString(EpochKey,"")==Epoch&&
                Data.GetLong(OpeningKey,0)==OpeningUntil&&Data.GetString(OpeningNonce,"")==OpenNonce;
        }
    }
}
