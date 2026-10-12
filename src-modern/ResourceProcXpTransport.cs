using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal static class ResourceProcXpTransport
    {
        internal const string MetadataRpc = "VM_ResourceProcEnvelopeV1", ReceiptRpc = "VM_ResourceProcReceiptV1";
        internal static readonly int DamageHash = "RPC_Damage".GetStableHashCode();
        private static readonly int MetadataHash = MetadataRpc.GetStableHashCode(), ReceiptHash = ReceiptRpc.GetStableHashCode();
        private static readonly Dictionary<ResourceProcEnvelope, Entry> Origins = new Dictionary<ResourceProcEnvelope, Entry>();
        private sealed class Entry { internal ResourceProcOrigin Origin; internal float Expires; }
        private static ZRoutedRpc Registered;
        [ThreadStatic] internal static ZRpc Incoming;
        [ThreadStatic] private static int Sending;
        internal sealed class NativeFrame
        { internal ResourceProcEnvelope Key; internal ResourceProcOrigin Origin; internal ResourceProcXpTicket Ticket; internal bool Continued; }
        [ThreadStatic] internal static NativeFrame Current;
        internal struct NativeState { internal NativeFrame Previous; }

        internal static void Reset() { Origins.Clear(); }
        internal static void Register()
        {
            var routed = ZRoutedRpc.instance;
            if (routed == null || ReferenceEquals(Registered, routed)) return;
            routed.Register<ZPackage>(MetadataRpc, Metadata);
            routed.Register<ZPackage>(ReceiptRpc, Receipt);
            Registered = routed;
        }
        private static Component Target(ZDOID uid)
        {
            GameObject instance = ZNetScene.instance?.FindInstance(uid);
            if (instance == null) return null;
            return (Component)instance.GetComponent<MineRock5>() ?? instance.GetComponent<TreeBase>() ??
                (Component)instance.GetComponent<TreeLog>() ?? instance.GetComponent<Destructible>();
        }
        private static ResourceProcEnvelope Key(ZRoutedRpc.RoutedRPCData data)
        {
            int index = -1;
            if (Target(data.m_targetZDO) is MineRock5)
            {
                var copy = new ZPackage(data.m_parameters.GetArray());
                index = copy.ReadInt();
            }
            return new ResourceProcEnvelope(FullResourceProcExperience.World, data.m_senderPeerID, data.m_msgID,
                data.m_targetZDO, data.m_methodHash, index);
        }
        internal static void BeginHandle(ZRoutedRpc.RoutedRPCData data, out NativeState state)
        {
            state = new NativeState { Previous = Current }; Current = null;
            if (data == null || data.m_methodHash != DamageHash || data.m_targetZDO == ZDOID.None || !FullResourceProcExperience.Enabled) return;
            try
            {
                var target = Target(data.m_targetZDO); var view = FullResourceProcExperience.View(target);
                if (target == null || FullResourceProcExperience.Kind(target) == 0 || view?.IsValid() != true || !view.IsOwner()) return;
                FullResourceProcExperience.EnsureState(); Register(); var key = Key(data);
                ResourceProcXpTicket carried = FullResourceProcExperience.Carried(key);
                if (carried != null) { Current = new NativeFrame { Key = key, Origin = carried.Origin, Ticket = carried, Continued = true }; return; }
                ResourceProcOrigin origin = FullResourceProcExperience.CaptureEnvelope(key, view.GetZDO().GetOwner(), target);
                if (origin == null && Origins.TryGetValue(key, out var entry))
                {
                    Origins.Remove(key); // One exact native envelope, including no-op executions.
                    if (entry.Expires >= Time.time) origin = entry.Origin;
                }
                if (origin != null) Current = new NativeFrame { Key = key, Origin = origin };
            }
            catch (Exception error) { Fault(error); }
        }
        internal static void FinishHandle(NativeState state) { Current = state.Previous; }

        internal static bool Managed(int method) => method == MetadataHash || method == ReceiptHash;
        // Only new messages are gated. Ordinary native damage/route behavior is unchanged.
        internal static bool AllowManaged(ZRoutedRpc.RoutedRPCData data)
        {
            if (data == null || !Managed(data.m_methodHash)) return true;
            ZNet net = ZNet.instance;
            if (net == null || !FullResourceProcExperience.Enabled) return false;
            if (Sending > 0 && data.m_senderPeerID == ZNet.GetUID()) return true;
            if (Incoming == null) return false;
            if (net.IsServer())
            {
                ZNetPeer peer = net.GetPeer(Incoming);
                return peer?.IsReady() == true && peer.m_uid == data.m_senderPeerID;
            }
            return ReferenceEquals(net.GetServerPeer()?.m_rpc, Incoming) && NetworkSync.HasServerSettings;
        }
        internal static bool BeforeRoute(ZRoutedRpc.RoutedRPCData data)
        {
            if (!AllowManaged(data)) return false;
            if (data == null || data.m_methodHash != DamageHash || FullResourceProcExperience.CurrentAttack == null) return true;
            try
            {
                var key = Key(data); Component target = Target(key.Target); var view = FullResourceProcExperience.View(target);
                if (target == null || view?.IsValid() != true || data.m_targetPeerID == ZNet.GetUID() ||
                    data.m_targetPeerID != view.GetZDO().GetOwner()) return true;
                ResourceProcOrigin origin = FullResourceProcExperience.CaptureEnvelope(key, data.m_targetPeerID, target);
                if (origin != null) Send(origin.Owner, MetadataRpc, Encode(origin));
            }
            catch (Exception error) { Fault(error); }
            return true;
        }
        private static void Send(long recipient, string method, ZPackage package)
        {
            Register();
            if (ZRoutedRpc.instance == null) return;
            try { Sending++; ZRoutedRpc.instance.InvokeRoutedRPC(recipient, method, package); }
            finally { Sending--; }
        }
        internal static void SendReceipt(ResourceProcOrigin origin, ResourceProcRow[] rows)
        {
            var packet = Encode(origin); packet.Write(rows.Length);
            foreach (var row in rows) { packet.Write(row.Segment.Source); packet.Write(row.Segment.Index); packet.Write(row.Prefab); packet.Write(row.Tier); }
            Send(origin.Envelope.Sender, ReceiptRpc, packet);
        }
        private static ZPackage Encode(ResourceProcOrigin origin)
        {
            var packet = new ZPackage();
            packet.Write(1); packet.Write(origin.Epoch); packet.Write(origin.Attack); packet.Write(origin.Author); packet.Write(origin.Character);
            packet.Write(origin.Owner); packet.Write((int)origin.Skill); packet.Write(origin.Tool); packet.Write(origin.ToolTier); packet.Write(origin.Raw);
            packet.Write(origin.Kind); packet.Write(origin.Maximum); packet.Write(origin.Prefab);
            packet.Write(origin.Envelope.World); packet.Write(origin.Envelope.Sender); packet.Write(origin.Envelope.Message);
            packet.Write(origin.Envelope.Target); packet.Write(origin.Envelope.Method); packet.Write(origin.Envelope.NativeIndex);
            return packet;
        }
        private static ResourceProcOrigin Decode(ZPackage packet)
        {
            if (packet == null || packet.Size() > 1024 * 1024 || packet.ReadInt() != 1) throw new FormatException("Invalid resource proc packet.");
            string epoch = packet.ReadString(); long attack = packet.ReadLong(), author = packet.ReadLong(); ZDOID character = packet.ReadZDOID();
            long owner = packet.ReadLong(); var skill = (Skills.SkillType)packet.ReadInt(); string tool = packet.ReadString();
            int tier = packet.ReadInt(); float raw = packet.ReadSingle(); int kind = packet.ReadInt(), maximum = packet.ReadInt(); string prefab = packet.ReadString();
            long world = packet.ReadLong(), sender = packet.ReadLong(), message = packet.ReadLong(); ZDOID target = packet.ReadZDOID();
            int method = packet.ReadInt(), index = packet.ReadInt();
            if (!Guid.TryParseExact(epoch, "N", out _) || attack <= 0 || author == 0 || character == ZDOID.None || owner == 0 ||
                tier < 1 || tier > 8 || !ResourceProcCredit.Finite(raw) || raw <= 0 || tool.Length == 0 || tool.Length > 96 ||
                prefab.Length == 0 || prefab.Length > 96 || maximum <= 0 || maximum > FullResourceProcExperience.MaximumRows ||
                world == 0 || world != FullResourceProcExperience.World || sender == 0 || message == 0 || target == ZDOID.None || method != DamageHash ||
                kind != 1 && kind != 2 || skill != (kind == 1 ? Skills.SkillType.Pickaxes : Skills.SkillType.WoodCutting) ||
                kind == 1 && (index < 0 || index >= maximum) || kind == 2 && index != -1) throw new FormatException("Invalid resource proc origin.");
            return new ResourceProcOrigin(epoch, attack, author, character, owner, skill, tool, tier, raw, kind, maximum,
                prefab, new ResourceProcEnvelope(world, sender, message, target, method, index));
        }
        private static void Metadata(long sender, ZPackage packet)
        {
            try
            {
                FullResourceProcExperience.EnsureState(); ResourceProcOrigin origin = Decode(packet);
                if (sender != origin.Envelope.Sender || origin.Owner != ZNet.GetUID()) return;
                var target = Target(origin.Envelope.Target); var view = FullResourceProcExperience.View(target);
                ZDO character = ZDOMan.instance?.GetZDO(origin.Character);
                if (view?.IsValid() != true || !view.IsOwner() || view.GetZDO().GetOwner() != origin.Owner ||
                    FullResourceProcExperience.Kind(target) != origin.Kind || TierDatabase.TargetKey(target) != origin.Prefab ||
                    FullResourceProcExperience.Maximum(target) != origin.Maximum || character == null ||
                    character.GetOwner() != sender || character.GetLong(ZDOVars.s_playerID, 0) != origin.Author ||
                    ZNetScene.instance?.GetPrefab(character.GetPrefab())?.GetComponent<Player>() == null) return;
                foreach (var key in new List<ResourceProcEnvelope>(Origins.Keys)) if (Origins[key].Expires < Time.time) Origins.Remove(key);
                if (Origins.Count >= 2048 || Origins.ContainsKey(origin.Envelope)) return;
                Origins.Add(origin.Envelope, new Entry { Origin = origin, Expires = Time.time + FullResourceProcExperience.Window });
            }
            catch (Exception error) { Fault(error); }
        }
        private static void Receipt(long sender, ZPackage packet)
        {
            try
            {
                ResourceProcOrigin origin = Decode(packet); int count = packet.ReadInt();
                if (sender != origin.Owner || count <= 0 || count > origin.Maximum) return;
                var rows = new ResourceProcRow[count];
                for (int i = 0; i < count; i++) rows[i] = new ResourceProcRow(packet.ReadZDOID(), packet.ReadInt(), packet.ReadString(), packet.ReadInt());
                FullResourceProcExperience.Receive(origin, rows, sender);
            }
            catch (Exception error) { Fault(error); }
        }
        internal static void Fault(Exception error)
        { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogWarning("[ResourceProcXP] " + error.GetType().Name + ": " + error.Message); }
    }
}
