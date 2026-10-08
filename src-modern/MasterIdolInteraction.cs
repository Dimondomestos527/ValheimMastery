using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterIdolInteraction
    {
        private static ZNet Session;
        private static readonly Dictionary<ZDOID, float> LastChange = new Dictionary<ZDOID, float>();
        private static float NextRequest;
        private static string RequestedToken;
        private static ZDOID RequestedId;private static long RequestedWorld,RequestedPlayer;
        internal static void Reset() { Session = null; LastChange.Clear(); NextRequest = 0; }
        internal static void Register(ZNetPeer peer)
        { peer?.m_rpc?.Register<ZPackage>("VM_Idol_Switch_v2", Receive);peer?.m_rpc?.Register<ZPackage>("VM_Idol_Switch_Result_v2",Reply); }
        internal static bool Request(Humanoid user, ZDO idol, bool hold)
        {
            var player = user as Player; var net = ZNet.instance;
            if (hold || player == null || player != Player.m_localPlayer || net == null || idol == null || player.IsDead() || player.IsTeleporting()) return false;
            if (Time.unscaledTime < NextRequest) return false;
            NextRequest = Time.unscaledTime + .5f;
            try
            {
                if(net.IsServer())MasterIdolWorldRegistry.Refresh();
                RequestedId=idol.m_uid;RequestedToken=idol.GetString(MasterIdolWorldRegistry.TokenKey,"");RequestedWorld=net.GetWorldUID();RequestedPlayer=player.GetPlayerID();
                int state = idol.GetInt(MasterIdolWorldRegistry.SwitchKey, 0);
                var packet = new ZPackage(); packet.Write(net.GetWorldUID()); packet.Write(player.GetPlayerID()); packet.Write(idol.m_uid);packet.Write(RequestedToken);
                packet.Write(state); packet.Write(!MasterIdolToggleRules.On(state));
                if (net.IsServer()) { packet.SetPos(0); Receive(null, packet); } else net.GetServerRPC()?.Invoke("VM_Idol_Switch_v2", packet);
                return true;
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[MasterIdols] Switch request deferred: " + error.Message); return false; }
        }
        private static void Reject(ZRpc rpc,long player,ZDOID id,string text)
        {
            var packet=new ZPackage();packet.Write(ZNet.instance.GetWorldUID());packet.Write(player);packet.Write(id);packet.Write(ZDOMan.instance?.GetZDO(id)?.GetString(MasterIdolWorldRegistry.TokenKey,"")??"");packet.Write(text??"Зачекай мить.");
            if(rpc==null){packet.SetPos(0);Reply(null,packet);}else rpc.Invoke("VM_Idol_Switch_Result_v2",packet);
        }
        private static void Reply(ZRpc rpc,ZPackage packet)
        {
            var net=ZNet.instance;var player=Player.m_localPlayer;
            if(net==null||player==null||packet==null||packet.Size()>1024||(net.IsServer()?rpc!=null:rpc!=net.GetServerRPC()))return;
            try
            {
                long world=packet.ReadLong(),owner=packet.ReadLong();var id=packet.ReadZDOID();string token=packet.ReadString();string text=packet.ReadString();
                if(world!=net.GetWorldUID()||world!=RequestedWorld||owner!=player.GetPlayerID()||owner!=RequestedPlayer||id!=RequestedId||token!=RequestedToken||text.Length>256)return;
                player.Message(MessageHud.MessageType.Center,"<color=#FFD36A>"+text+"</color>");
            }
            catch { }
        }
        private static void Trace(string reason)
        { if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogWarning("[MasterIdols] switch denied="+reason+"; "+MasterIdolWorldIndex.Diagnostics()); }
        private static void Receive(ZRpc rpc, ZPackage packet)
        {
            var net = ZNet.instance;
            if (net?.IsServer() != true || packet == null || packet.Size() > 256) return;
            if (Session != net) { Reset(); Session = net; }
            try
            {
                if (packet.ReadLong() != net.GetWorldUID()) return;
                long player = packet.ReadLong(); var id = packet.ReadZDOID(); string token=packet.ReadString(); int expected = packet.ReadInt(); bool desired = packet.ReadBool();
                var actor = WorkshopActor.Resolve(rpc); var idol = ZDOMan.instance?.GetZDO(id);
                if (actor?.Available != true || actor.GetPlayerID() != player || actor.IsDead() || actor.IsTeleporting() || idol == null ||
                    MasterIdolWorldRegistry.Profile(idol.GetPrefab()) == null || !Guid.TryParseExact(token,"N",out _) || idol.GetString(MasterIdolWorldRegistry.TokenKey,"")!=token || Vector3.Distance(actor.Position, idol.GetPosition()) > 4f) return;
                string type = MasterIdolWorldRegistry.Profile(idol.GetPrefab()).Id;
                if (LastChange.TryGetValue(id, out float last) && Time.unscaledTime - last < .75f) return;
                if(LastChange.Count>=4096)
                {var expired=new List<ZDOID>();foreach(var e in LastChange)if(Time.unscaledTime-e.Value>=.75f)expired.Add(e.Key);foreach(var key in expired)LastChange.Remove(key);if(LastChange.Count>=4096)return;}
                LastChange[id] = Time.unscaledTime; // per-output throttle, bounded; different bases do not block one another
                if (!MasterIdolWorldIndex.WardAllows(player, idol.GetPosition())) { Trace("ward");Reject(rpc,player,id,"Оберіг не дозволяє тобі торкнутися цього благословення."); return; }
                if (!MasterIdolWorldRegistry.SetSwitch(idol, expected, desired)) { Trace(MasterIdolWorldRegistry.SwitchFailure+"; output="+id+" type="+type+" desired="+desired+" expected="+expected+" mirrored="+idol.GetInt(MasterIdolWorldRegistry.SwitchKey,0));Reject(rpc,player,id,MasterIdolWorldRegistry.SwitchDenial); return; }
                // Registry publishes the changed ZDO after durable switch completion.
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[MasterIdols] Switch rejected: " + error.Message); }
        }
    }
}
