using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Report actual HP loss only from the native network owner of the victim.
    // Server retains the gauge, eligibility, cooldown and activation decisions.
    internal static class Fists70HitRelay
    {
        private static int Sequence;
        private static ZNet Session;
        private static readonly Dictionary<ZRpc, int> Last = new Dictionary<ZRpc, int>();
        internal static void Register(ZRpc rpc)
        {
            if (Session != ZNet.instance) { Last.Clear(); Sequence = 0; Session = ZNet.instance; }
            rpc.Register<ZPackage>("VM_FistsConfirmedHit", Receive);
        }
        internal static void Send(Character victim, HitData hit, float actual, bool parried)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || victim?.m_nview?.IsOwner() != true ||
                actual <= .001f || !float.IsFinite(actual) || PerkRuntimeService.IsPerkGenerated(hit) ||
                MasteryAttackTagService.Has(hit, MasteryAttackTag.Fists70Maul) ||
                (!(victim is Player) && hit.m_skill != Skills.SkillType.Unarmed)) return;
            if (victim is Player && !Fists70MaulService.IsDirectAttackHit(hit)) return;
            OwnerSkillAuthority.SendNow();
            ZPackage package = new ZPackage();
            package.Write(++Sequence); package.Write(victim.GetZDOID()); package.Write(actual); package.Write(parried);
            hit.Serialize(ref package);
            ZNet.instance.GetServerRPC()?.Invoke("VM_FistsConfirmedHit", package);
        }
        private static void Receive(ZRpc rpc, ZPackage package)
        {
            if (ZNet.instance?.IsServer() != true) return;
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            if (peer?.IsReady() != true) return;
            try
            {
                int sequence = package.ReadInt(); ZDOID id = package.ReadZDOID();
                float actual = package.ReadSingle(); bool parried = package.ReadBool();
                HitData hit = new HitData(); hit.Deserialize(ref package);
                Character victim = ZNetScene.instance?.FindInstance(id)?.GetComponent<Character>();
                Character attacker = hit.GetAttacker();
                if (sequence <= 0 || (Last.TryGetValue(rpc, out int previous) && sequence <= previous) ||
                    victim?.m_nview?.GetZDO()?.GetOwner() != peer.m_uid || attacker == null ||
                    !float.IsFinite(actual) || actual <= .001f || actual > victim.GetMaxHealth() ||
                    !float.IsFinite(hit.GetTotalDamage()) || hit.GetTotalDamage() <= 0f ||
                    (!(victim is Player) && (hit.m_skill != Skills.SkillType.Unarmed || !(attacker is Player))) ||
                    (!(victim is Player) && Vector3.Distance(victim.GetCenterPoint(), attacker.GetCenterPoint()) > 12f)) return;
                Last[rpc] = sequence;
                if (Last.Count > 64)
                    foreach (var key in new List<ZRpc>(Last.Keys)) if (ZNet.instance.GetPeer(key) == null) Last.Remove(key);
                if (parried && victim is Player player) Fists70MaulService.MarkPerfectParry(player, attacker);
                Fists70MaulService.ObserveConfirmedDamage(victim, hit, actual);
            }
            catch (Exception error)
            { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogWarning("[Fists70] hit report rejected: " + error.GetType().Name); }
        }
    }
}
