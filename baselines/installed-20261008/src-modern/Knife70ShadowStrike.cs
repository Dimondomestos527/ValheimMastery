using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal sealed class Knife70ShadowRequest
    {
        internal Player Source;
        internal Character Target;
        internal HitData.DamageTypes Damage;
        internal int Generation;
        internal int AnimationStateHash;
        internal Vector3 SpawnPosition;
        internal Vector3 VisualTargetAtQueue;
        internal Vector3 VisualOrigin;
        internal bool OtherTargetBranch;
        internal float ExecuteAt;
    }

    internal sealed class Knife70ShadowRunner : MonoBehaviour
    {
        private void Update() => Knife70ShadowStrikeService.Tick();
        private void OnDestroy() => Knife70ShadowStrikeService.RunnerDestroyed(this);
    }

    internal static class Knife70ShadowStrikeService
    {
        internal const string PerkId = "Knives70ShadowStrike";
        private const int EventsPerFrame = 8;
        private const float InitialDelay = 0.15f;
        private const float RecursiveDelay = 0.12f;
        private const float SearchRadius = 10f;
        private const float DefaultChance = 0.35f;
        private const int DebugChainBudget = 64;
        private static readonly Queue<Knife70ShadowRequest> Queue = new Queue<Knife70ShadowRequest>();
        private static Knife70ShadowRunner Runner;
        private static float InitialChance = DefaultChance;
        private static float SameChance = DefaultChance;
        private static float OtherChance = DefaultChance;
        private static bool DebugEnabled;
        private static bool DebugChain;
        private static int DebugEnqueueBudget;
        private static int Processed;
        private static int LastGeneration;
        private static string LastTarget = "-";
        private static bool LastSameRoll;
        private static bool LastOtherRoll;

        internal static void TryInitial(Player source, Character target, HitData.DamageTypes sourceDamage, Vector3 hitPoint, int animationStateHash)
        {
            if (!IsAuthoritative() || !ValidSource(source) || !ValidTarget(source, target) || sourceDamage.GetTotalDamage() <= 0f) return;
            float roll = UnityEngine.Random.value;
            bool proc = roll < Mathf.Clamp01(InitialChance);
            if (DebugEnabled)
                MasteryPlugin.Log.LogInfo("[Knife70] InitialRoll=" + roll.ToString("0.000", CultureInfo.InvariantCulture) + " Proc=" + proc + " Target=" + SafeName(target));
            if (!proc) { SendDebug(source); return; }
            HitData.DamageTypes shadowDamage = sourceDamage;
            shadowDamage.Modify(0.50f);
            Enqueue(source, target, shadowDamage, 1, animationStateHash, hitPoint, false, InitialDelay);
        }

        internal static void ForceInitial(Player source, Character target)
        {
            if (!IsAuthoritative() || !ValidSource(source) || !ValidTarget(source, target)) return;
            ItemDrop.ItemData weapon = source.GetCurrentWeapon();
            if (weapon?.m_shared?.m_skillType != Skills.SkillType.Knives) return;
            HitData.DamageTypes damage = weapon.GetDamage();
            float skillFactor = source.GetSkills().GetRandomSkillFactor(Skills.SkillType.Knives);
            damage.Modify(Mathf.Max(0f, skillFactor * 0.50f));
            int stateHash = source.m_animator != null ? source.m_animator.GetCurrentAnimatorStateInfo(0).fullPathHash : 0;
            Enqueue(source, target, damage, 1, stateHash, source.GetCenterPoint(), false, InitialDelay);
        }

        private static void Enqueue(Player source, Character target, HitData.DamageTypes damage, int generation, int animationStateHash, Vector3 visualOrigin, bool otherBranch, float delay)
        {
            if (!ValidSource(source) || !ValidTarget(source, target)) return;
            if (DebugChain)
            {
                if (DebugEnqueueBudget <= 0) return;
                DebugEnqueueBudget--;
            }
            EnsureRunner();
            Vector3 spawn = ChooseVisualSpawn(source, target);
            Knife70ShadowRequest request = new Knife70ShadowRequest
            {
                Source = source,
                Target = target,
                Damage = damage,
                Generation = generation,
                AnimationStateHash = animationStateHash,
                SpawnPosition = spawn,
                VisualTargetAtQueue = target.GetCenterPoint(),
                VisualOrigin = visualOrigin,
                OtherTargetBranch = otherBranch,
                ExecuteAt = Time.time + Mathf.Max(0.02f, delay)
            };
            Queue.Enqueue(request);
        }

        internal static void Tick()
        {
            if (!IsAuthoritative())
            {
                if (Queue.Count > 0) Queue.Clear();
                return;
            }
            int count = 0;
            while (count < EventsPerFrame && Queue.Count > 0 && Queue.Peek().ExecuteAt <= Time.time)
            {
                Execute(Queue.Dequeue());
                count++;
            }
            if (DebugChain && DebugEnqueueBudget <= 0 && Queue.Count == 0)
            {
                DebugChain = false;
                InitialChance = SameChance = OtherChance = DefaultChance;
                MasteryPlugin.Log.LogWarning("[Knife70Shadow] DEBUG chain diagnostic ended after " + DebugChainBudget + " queued events; chances reset to 0.35.");
            }
        }

        private static void Execute(Knife70ShadowRequest request)
        {
            if (request == null || !ValidSource(request.Source) || !ValidTarget(request.Source, request.Target)) return;
            HitData hit = new HitData
            {
                m_skill = Skills.SkillType.Knives,
                m_point = request.Target.GetCenterPoint(),
                m_dir = (request.Target.GetCenterPoint() - request.SpawnPosition).normalized,
                m_damage = request.Damage,
                m_staggerMultiplier = 0f,
                m_pushForce = 0f,
                m_backstabBonus = 1f,
                m_ranged = false
            };
            hit.SetAttacker(request.Source);
            MasteryAttackTagService.Add(hit, MasteryAttackTag.Knives70Shadow);
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.PerkId = PerkId;
            context.SourceSkill = Skills.SkillType.Knives;
            context.SourcePlayerId = request.Source.GetPlayerID();
            context.GenerationDepth = request.Generation;
            context.IsPerkGenerated = true;
            context.AllowSelfProc = false;
            context.AllowOtherPerkProc = false;
            context.AllowKnife70InitialProc = false;
            context.AllowShadowRecursion = true;
            context.IgnoreReflect = true;
            context.IgnoreExecution = true;
            context.IgnoreOverdrawPayload = true;

            // Damage may kill or destroy the target. Capture visual origin before
            // calling it, then revalidate references for optional child branches.
            Vector3 parentPoint = request.Target.GetCenterPoint();
            string visual = PackVisual(request, parentPoint);
            request.Target.Damage(hit);
            // Cosmetic notification is for a dispatched valid shadow, not a queued
            // attempt. Remote victim ApplyDamage acknowledgement is NOT implied.
            try { NetworkSync.BroadcastKnife70Visual(visual); }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Knife70VFX] Cosmetic dispatch failed: " + error.GetType().Name); }
            Processed++;
            LastGeneration = request.Generation;
            LastTarget = SafeName(request.Target);

            float sameRoll = UnityEngine.Random.value;
            float otherRoll = UnityEngine.Random.value;
            LastSameRoll = sameRoll < Mathf.Clamp01(SameChance);
            LastOtherRoll = otherRoll < Mathf.Clamp01(OtherChance);

            if (LastSameRoll && request.Target != null && !request.Target.IsDead())
                Enqueue(request.Source, request.Target, request.Damage, request.Generation + 1, request.AnimationStateHash, parentPoint, false, RecursiveDelay);

            if (LastOtherRoll)
            {
                Character other = ChooseOtherTarget(request.Source, request.Target, parentPoint);
                if (other != null)
                    Enqueue(request.Source, other, request.Damage, request.Generation + 1, request.AnimationStateHash, parentPoint, true, RecursiveDelay);
            }

            if (DebugEnabled)
                MasteryPlugin.Log.LogInfo("[Knife70Shadow] Generation=" + request.Generation + " Target=" + LastTarget + " Damage=" + request.Damage.GetTotalDamage().ToString("0.0", CultureInfo.InvariantCulture) + " SameRoll=" + LastSameRoll + " OtherRoll=" + LastOtherRoll + " QueueAfter=" + Queue.Count);
            SendDebug(request.Source);
        }

        private static Character ChooseOtherTarget(Player source, Character current, Vector3 origin)
        {
            List<Character> candidates = new List<Character>();
            List<float> weights = new List<float>();
            float total = 0f;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate == current || !ValidTarget(source, candidate)) continue;
                float distance = Vector3.Distance(origin, candidate.GetCenterPoint());
                if (distance > SearchRadius) continue;
                float weight = 1f / (1f + distance);
                candidates.Add(candidate); weights.Add(weight); total += weight;
            }
            if (candidates.Count == 0 || total <= 0f) return null;
            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < candidates.Count; ++i)
            {
                roll -= weights[i];
                if (roll <= 0f) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private static Vector3 ChooseVisualSpawn(Player source, Character target)
        {
            Vector3 center = target.transform.position;
            Vector3 back = -target.transform.forward; back.y = 0f;
            if (back.sqrMagnitude < 0.01f) { back = source.transform.position - center; back.y = 0f; }
            back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
            Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
            float sideSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            float distance = Mathf.Clamp(target.GetRadius() + 0.85f, 1.0f, 2.6f);
            Vector3[] probes =
            {
                center + back * 0.45f + side * sideSign * distance,
                center + back * 0.75f - side * sideSign * distance,
                center + back * distance
            };
            foreach (Vector3 probe in probes)
            {
                Vector3 candidate = probe;
                if (Physics.Raycast(probe + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 7f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    candidate = ground.point + Vector3.up * 0.03f;
                Vector3 rayOrigin = target.GetCenterPoint();
                Vector3 ray = candidate + Vector3.up * 0.7f - rayOrigin;
                if (ray.sqrMagnitude > 0.1f && Physics.Raycast(rayOrigin, ray.normalized, out RaycastHit wall, ray.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    Character owner = wall.collider?.GetComponentInParent<Character>();
                    if (owner != target && owner != source) continue;
                }
                return candidate;
            }
            return center + back * distance;
        }

        private static bool ValidSource(Player source) => source != null && !source.IsDead() && source.GetCurrentWeapon()?.m_shared?.m_skillType == Skills.SkillType.Knives && PerkRuntimeService.HasPerk(source, Skills.SkillType.Knives, 70);
        private static bool ValidTarget(Player source, Character target) => target != null && target != source && !target.IsDead() && !(target is Player) && BaseAI.IsEnemy(source, target);
        private static bool IsAuthoritative() => ZNet.instance != null && ZNet.instance.IsServer();
        private static string SafeName(Character target) => target != null && target.gameObject != null ? target.gameObject.name.Replace("(Clone)", "") : "invalid";

        private static void EnsureRunner()
        {
            if (Runner != null) return;
            GameObject go = new GameObject("ValheimMastery_Knife70ShadowQueue");
            Runner = go.AddComponent<Knife70ShadowRunner>();
        }

        internal static void RunnerDestroyed(Knife70ShadowRunner runner)
        {
            if (Runner == runner) Runner = null;
        }

        internal static void Reset()
        {
            Queue.Clear(); Processed = 0; LastGeneration = 0; LastTarget = "-"; DebugChain = false; DebugEnqueueBudget = 0;
            InitialChance = SameChance = OtherChance = DefaultChance;
            Knife70ShadowRunner oldRunner = Runner;
            Runner = null;
            if (oldRunner != null) UnityEngine.Object.Destroy(oldRunner.gameObject);
            Knife70ShadowVisualService.ClearAll();
        }

        internal static string HandleDebugCommand(Player source, string command)
        {
            if (!MasteryPlugin.Settings.UIDebugLogging.Value) return "Knife70 debug is disabled in config.";
            if (string.IsNullOrWhiteSpace(command)) return DebugSummary();
            string[] p = command.Split(':');
            string action = p[0].ToLowerInvariant();
            if (action == "chance" || action == "samechance" || action == "otherchance")
            {
                if (p.Length != 2 || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return "Expected value 0..1.";
                value = Mathf.Clamp01(value);
                if (action == "chance") InitialChance = value;
                else if (action == "samechance") SameChance = value;
                else OtherChance = value;
                return action + "=" + value.ToString("0.###", CultureInfo.InvariantCulture);
            }
            if (action == "debug")
            {
                DebugEnabled = p.Length > 1 && p[1].Equals("on", StringComparison.OrdinalIgnoreCase);
                return "Knife70 debug=" + DebugEnabled;
            }
            if (action == "chain")
            {
                DebugEnabled = true; DebugChain = true; DebugEnqueueBudget = DebugChainBudget;
                InitialChance = SameChance = OtherChance = 1f;
                return "Knife70 DEBUG chain armed: 100%/100%/100%, visual/gameplay queue budget=" + DebugChainBudget + ". Use vm knife70 stop.";
            }
            if (action == "stop")
            {
                Queue.Clear(); DebugChain = false; DebugEnqueueBudget = 0; InitialChance = SameChance = OtherChance = DefaultChance;
                return "Knife70 queue stopped; chances reset to 0.35.";
            }
            if (action == "shadow" && p.Length == 3 && long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) && uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
            {
                Character target = ZNetScene.instance?.FindInstance(new ZDOID(user, id))?.GetComponent<Character>();
                if (!ValidTarget(source, target)) return "Knife70 force shadow rejected: invalid target.";
                ForceInitial(source, target);
                return "Knife70 forced shadow queued on " + SafeName(target) + ".";
            }
            if (action == "state") return DebugSummary();
            return "Unknown knife70 command.";
        }

        internal static string PackHitCandidate(Player source, Character target, HitData.DamageTypes damage, Vector3 point, int animationHash)
        {
            if (source == null || target == null) return "";
            ZDOID id = target.GetZDOID();
            return string.Join(";", new[]
            {
                source.GetPlayerID().ToString(CultureInfo.InvariantCulture), id.UserID.ToString(CultureInfo.InvariantCulture), id.ID.ToString(CultureInfo.InvariantCulture),
                F(damage.m_damage), F(damage.m_blunt), F(damage.m_slash), F(damage.m_pierce), F(damage.m_chop), F(damage.m_pickaxe),
                F(damage.m_fire), F(damage.m_frost), F(damage.m_lightning), F(damage.m_poison), F(damage.m_spirit),
                F(point.x), F(point.y), F(point.z), animationHash.ToString(CultureInfo.InvariantCulture)
            });
        }

        internal static void HandleHitCandidate(string payload)
        {
            if (!IsAuthoritative() || string.IsNullOrEmpty(payload)) return;
            string[] p = payload.Split(';');
            if (p.Length != 18 || !long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceId) ||
                !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) ||
                !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id) ||
                !TryFloat(p[3], out float generic) || !TryFloat(p[4], out float blunt) || !TryFloat(p[5], out float slash) ||
                !TryFloat(p[6], out float pierce) || !TryFloat(p[7], out float chop) || !TryFloat(p[8], out float pickaxe) ||
                !TryFloat(p[9], out float fire) || !TryFloat(p[10], out float frost) || !TryFloat(p[11], out float lightning) ||
                !TryFloat(p[12], out float poison) || !TryFloat(p[13], out float spirit) ||
                !TryFloat(p[14], out float x) || !TryFloat(p[15], out float y) || !TryFloat(p[16], out float z) ||
                !int.TryParse(p[17], NumberStyles.Integer, CultureInfo.InvariantCulture, out int animationHash)) return;
            Player source = Player.GetPlayer(sourceId);
            Character target = ZNetScene.instance?.FindInstance(new ZDOID(user, id))?.GetComponent<Character>();
            HitData.DamageTypes damage = new HitData.DamageTypes
            {
                m_damage = generic, m_blunt = blunt, m_slash = slash, m_pierce = pierce, m_chop = chop, m_pickaxe = pickaxe,
                m_fire = fire, m_frost = frost, m_lightning = lightning, m_poison = poison, m_spirit = spirit
            };
            TryInitial(source, target, damage, new Vector3(x, y, z), animationHash);
        }

        private static bool TryFloat(string raw, out float value) => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        internal static string DebugSummary() => "Queue=" + Queue.Count + " Processed=" + Processed + " Generation=" + LastGeneration + " Target=" + LastTarget + " SameRoll=" + LastSameRoll + " OtherRoll=" + LastOtherRoll + " VisualActive=" + Knife70ShadowVisualService.ActiveCount + " chances=" + InitialChance.ToString("0.##") + "/" + SameChance.ToString("0.##") + "/" + OtherChance.ToString("0.##");

        private static void SendDebug(Player source)
        {
            if (!DebugEnabled || source == null) return;
            string state = DebugSummary();
            if (source == Player.m_localPlayer) Knife70DebugOverlay.Set(state, true);
            else NetworkSync.SendKnife70DebugState(source, state);
        }

        private static string PackVisual(Knife70ShadowRequest r, Vector3 contact)
        {
            ZDOID target = r.Target.GetZDOID();
            return string.Join(";", new[]
            {
                r.Source.GetPlayerID().ToString(CultureInfo.InvariantCulture), target.UserID.ToString(CultureInfo.InvariantCulture), target.ID.ToString(CultureInfo.InvariantCulture),
                F((r.SpawnPosition + contact - r.VisualTargetAtQueue).x), F((r.SpawnPosition + contact - r.VisualTargetAtQueue).y), F((r.SpawnPosition + contact - r.VisualTargetAtQueue).z), F(r.VisualOrigin.x), F(r.VisualOrigin.y), F(r.VisualOrigin.z),
                F(contact.x), F(contact.y), F(contact.z), r.Generation.ToString(CultureInfo.InvariantCulture),
                r.OtherTargetBranch ? "1" : "0", r.AnimationStateHash.ToString(CultureInfo.InvariantCulture)
            });
        }
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.Last)]
    internal static class Knife70InitialProcPatch
    {
        private sealed class State
        {
            internal Player Source;
            internal HitData.DamageTypes Damage;
            internal float Health;
            internal Vector3 Point;
            internal int AnimationStateHash;
        }

        private static void Prefix(Character __instance, HitData hit, out State __state)
        {
            __state = null;
            if (ZNet.instance == null || __instance == null || hit == null || __instance.IsDead() ||
                hit.m_skill != Skills.SkillType.Knives || hit.m_ranged || hit.GetTotalDamage() <= 0f ||
                PerkRuntimeService.IsPerkGenerated(hit) || MasteryAttackTagService.Has(hit, MasteryAttackTag.ShadowStrike) ||
                MasteryAttackTagService.Has(hit, MasteryAttackTag.Knives70Shadow)) return;
            Player source = hit.GetAttacker() as Player;
            if (source == null || !BaseAI.IsEnemy(source, __instance)) return;
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            if (context != null && (!context.AllowKnife70InitialProc || context.IsPerkGenerated)) return;
            __state = new State
            {
                Source = source,
                Damage = hit.m_damage,
                Health = __instance.GetHealth(),
                Point = hit.m_point,
                AnimationStateHash = source.m_animator != null ? source.m_animator.GetCurrentAnimatorStateInfo(0).fullPathHash : 0
            };
        }

        private static void Postfix(Character __instance, State __state)
        {
            if (__state == null || __instance == null || __instance.IsDead() || __instance.GetHealth() >= __state.Health - 0.001f) return;
            if (ZNet.instance != null && ZNet.instance.IsServer())
                Knife70ShadowStrikeService.TryInitial(__state.Source, __instance, __state.Damage, __state.Point, __state.AnimationStateHash);
            else
                NetworkSync.SendKnife70HitCandidate(Knife70ShadowStrikeService.PackHitCandidate(__state.Source, __instance, __state.Damage, __state.Point, __state.AnimationStateHash));
        }
    }

    internal static class Knife70ShadowVisualService
    {
        private const int MaximumActiveEchoes = 16;
        private const int MaximumPooledEchoes = 8;
        // Every echo uses the same native Wraith visual, so retaining a separate
        // collection of that same model for every player only wastes memory.
        private static readonly Stack<Knife70ShadowEchoVisual> Pool = new Stack<Knife70ShadowEchoVisual>();
        private static readonly HashSet<Knife70ShadowEchoVisual> Active = new HashSet<Knife70ShadowEchoVisual>();
        private static readonly Stack<Knife70ShadowStreak> StreakPool = new Stack<Knife70ShadowStreak>();
        private static readonly Stack<Knife70ShadowImpact> ImpactPool = new Stack<Knife70ShadowImpact>();
        private static readonly HashSet<Knife70ShadowImpact> ActiveImpacts = new HashSet<Knife70ShadowImpact>();
        private static float LastAudioAt;
        private static float LastImpactAudioAt;
        internal static int ActiveCount => Active.Count + ActiveImpacts.Count;

        internal static bool TryHandle(string payload)
        {
            string[] p = payload?.Split(';');
            if (p == null || p.Length != 15) return false;
            if (!long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long sourceId) ||
                !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long targetUser) ||
                !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint targetId) ||
                !TryV(p, 3, out Vector3 spawn) || !TryV(p, 6, out Vector3 origin) || !TryV(p, 9, out Vector3 target) ||
                !int.TryParse(p[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out int generation) ||
                !int.TryParse(p[14], NumberStyles.Integer, CultureInfo.InvariantCulture, out int animationHash)) return false;
            bool otherBranch = p[13] == "1";
            Character tracked = ZNetScene.instance?.FindInstance(new ZDOID(targetUser, targetId))?.GetComponent<Character>();
            Play(sourceId, spawn, origin, target, generation, otherBranch, animationHash, tracked);
            return true;
        }

        private static bool TryV(string[] p, int i, out Vector3 v)
        {
            v = Vector3.zero;
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(p[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(p[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;
            v = new Vector3(x, y, z); return true;
        }

        private static void Play(long sourceId, Vector3 spawn, Vector3 origin, Vector3 target, int generation, bool otherBranch, int animationHash, Character tracked)
        {
            // Contact remains visible/audible even when the full-ghost budget is exhausted.
            PlayImpact(target, generation);
            if (Time.time - LastAudioAt >= 0.12f)
            {
                PlayShadowAudio(spawn, generation <= 1 ? 0.55f : 0.35f, generation <= 1 ? 0.88f : 0.82f);
                if (generation <= 1)
                    PerkAudioService.Play("knives_70_spirit_arrival", "sfx_wraith_attack", spawn, 0.70f,
                        volumeScale: 0.16f, pitchScale: 1.12f);
                LastAudioAt = Time.time;
            }
            // Cosmetic overload protection only; the authoritative damage queue
            // and every recursion roll run independently of this visual service.
            if (otherBranch && generation > 1 && Active.Count < MaximumActiveEchoes)
            {
                SpawnVanillaEffect("vfx_ShadowPerson_hit", Vector3.Lerp(origin, target, .30f), .24f, .18f);
                SpawnVanillaEffect("vfx_ShadowPerson_hit", Vector3.Lerp(origin, target, .65f), .18f, .18f);
            }
            int ownEchoes = 0;
            foreach (var active in Active) if (active != null && active.OwnerId == sourceId) ownEchoes++;
            if (Active.Count >= MaximumActiveEchoes || ownEchoes >= 4) return;
            Knife70ShadowEchoVisual echo = AcquireEcho();
            if (echo == null) return;
            Active.Add(echo);
            try { echo.Activate(sourceId, spawn, target, animationHash, generation, tracked); }
            catch
            {
                Active.Remove(echo);
                UnityEngine.Object.Destroy(echo.gameObject);
                throw;
            }
        }

        private static void PlayShadowAudio(Vector3 position, float volumeScale, float pitchScale)
        {
            PerkAudioService.Play("knives_70_blade", "sfx_knife_swing", position, 0.10f,
                volumeScale: volumeScale, pitchScale: pitchScale);
        }

        private static Knife70ShadowEchoVisual AcquireEcho()
        {
            while (Pool.Count > 0)
            {
                Knife70ShadowEchoVisual pooled = Pool.Pop();
                if (pooled != null) return pooled;
            }
            GameObject clone = new GameObject("ValheimMastery_Knife70FacelessAssassin");
            clone.SetActive(false);
            Knife70ShadowEchoVisual echo = clone.AddComponent<Knife70ShadowEchoVisual>();
            try { echo.Configure(); }
            catch { UnityEngine.Object.Destroy(clone); throw; }
            return echo;
        }

        internal static void Release(Knife70ShadowEchoVisual echo, long sourceId)
        {
            if (echo == null) return;
            Active.Remove(echo);
            echo.gameObject.SetActive(false);
            if (Pool.Count < MaximumPooledEchoes) Pool.Push(echo); else UnityEngine.Object.Destroy(echo.gameObject);
        }

        internal static void PlayImpact(Vector3 position, int generation)
        {
            SpawnVanillaEffect("vfx_wraith_hit", position, generation <= 1 ? 0.95f : 0.62f);
            SpawnVanillaEffect("vfx_HitSparks", position, generation <= 1 ? 0.50f : 0.30f);
            if (Time.time - LastImpactAudioAt >= 0.25f)
            {
                PerkAudioService.Play("knives_70_strike", "sfx_sword_hit", position, 0.12f,
                    volumeScale: generation <= 1 ? 0.45f : 0.30f, pitchScale: 1.08f);
                if (generation <= 1)
                    PerkAudioService.Play("knives_70_spirit_strike", "sfx_wraith_attack_hit", position, 0.24f,
                        volumeScale: 0.18f, pitchScale: 1.10f);
                LastImpactAudioAt = Time.time;
            }
        }

        internal static void PlayVanish(Vector3 position, int generation)
        {
            if (generation <= 1) SpawnVanillaEffect("vfx_wraith_death", position, .22f, .18f);
        }

        internal static void SpawnVanillaEffect(string prefabName, Vector3 position, float scale, float lifetime = .30f)
        {
            PerkNativeFeedback.PlayVfx(prefabName, position, scale, lifetime);
        }

        internal static void Release(Knife70ShadowImpact impact)
        {
            if (impact == null) return;
            ActiveImpacts.Remove(impact);
            impact.gameObject.SetActive(false);
            if (ImpactPool.Count < 24) ImpactPool.Push(impact); else UnityEngine.Object.Destroy(impact.gameObject);
        }

        private static Knife70ShadowStreak AcquireStreak()
        {
            while (StreakPool.Count > 0) { Knife70ShadowStreak pooled = StreakPool.Pop(); if (pooled != null) return pooled; }
            GameObject go = new GameObject("ValheimMastery_Knife70ShadowStreak");
            return go.AddComponent<Knife70ShadowStreak>();
        }

        internal static void Release(Knife70ShadowStreak streak)
        {
            if (streak == null) return;
            streak.gameObject.SetActive(false);
            if (StreakPool.Count < 24) StreakPool.Push(streak); else UnityEngine.Object.Destroy(streak.gameObject);
        }

        internal static void ClearAll()
        {
            LastAudioAt = float.NegativeInfinity;
            LastImpactAudioAt = float.NegativeInfinity;
            foreach (Knife70ShadowEchoVisual echo in new List<Knife70ShadowEchoVisual>(Active)) if (echo != null) UnityEngine.Object.Destroy(echo.gameObject);
            Active.Clear();
            foreach (Knife70ShadowImpact impact in new List<Knife70ShadowImpact>(ActiveImpacts)) if (impact != null) UnityEngine.Object.Destroy(impact.gameObject);
            ActiveImpacts.Clear();
            while (Pool.Count > 0) { Knife70ShadowEchoVisual echo = Pool.Pop(); if (echo != null) UnityEngine.Object.Destroy(echo.gameObject); }
            while (StreakPool.Count > 0) { Knife70ShadowStreak streak = StreakPool.Pop(); if (streak != null) UnityEngine.Object.Destroy(streak.gameObject); }
            while (ImpactPool.Count > 0) { Knife70ShadowImpact impact = ImpactPool.Pop(); if (impact != null) UnityEngine.Object.Destroy(impact.gameObject); }
        }
    }

    #if false // Retained only as implementation history; custom materials produced pink/missing-shader visuals in Valheim 1.0.
    internal sealed class Knife70ShadowEchoVisual : MonoBehaviour
    {
        private readonly List<Material> Materials = new List<Material>();
        private readonly List<Color> BaseColors = new List<Color>();
        private Transform BodyRoot;
        private Animator ShadowAnimator;
        private Transform LeftArm;
        private Transform RightArm;
        private TrailRenderer LeftBladeTrail;
        private TrailRenderer RightBladeTrail;
        private LineRenderer LeftBlade;
        private LineRenderer RightBlade;
        private ParticleSystem ShadowMotes;
        private LineRenderer GroundRift;
        private LineRenderer SlashArc;
        private float Born;
        private long SourceId;
        private bool Active;
        private bool ImpactPlayed;
        private bool DissolvePlayed;
        private Vector3 TargetPosition;
        private int Generation;

        internal void Configure(Player source)
        {
            BuildAssassin(source);
            BuildVfx();
            gameObject.SetActive(false);
        }

        private void BuildAssassin(Player source)
        {
            GameObject visual = UnityEngine.Object.Instantiate(source.m_visual);
            visual.name = "organic_shadow_silhouette";
            BodyRoot = visual.transform;
            BodyRoot.SetParent(transform, false);
            BodyRoot.localPosition = Vector3.zero;
            BodyRoot.localRotation = Quaternion.identity;
            BodyRoot.localScale = Vector3.one;

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody rigidbody in visual.GetComponentsInChildren<Rigidbody>(true)) { rigidbody.detectCollisions = false; rigidbody.isKinematic = true; }
            ShadowAnimator = visual.GetComponentInChildren<Animator>(true);
            foreach (Behaviour behaviour in visual.GetComponentsInChildren<Behaviour>(true))
                if (behaviour != ShadowAnimator) behaviour.enabled = false;

            Color body = new Color(0.018f, 0.022f, 0.035f, 0.82f);
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.materials;
                for (int i = 0; i < materials.Length; ++i)
                {
                    Material material = materials[i];
                    if (material == null) { material = CreateMaterial("fx_perfectdodge", body, false); materials[i] = material; }
                    if (material == null) continue;
                    if (material.HasProperty("_Color")) material.color = body;
                    if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", new Color(0.06f, 0.012f, 0.11f, 1f));
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON"); material.EnableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
                    Materials.Add(material); BaseColors.Add(body);
                }
                renderer.materials = materials;
            }

            LeftArm = new GameObject("left_phantom_arm").transform;
            LeftArm.SetParent(BodyRoot, false); LeftArm.localPosition = new Vector3(-0.43f, 1.10f, 0.08f);
            RightArm = new GameObject("right_phantom_arm").transform;
            RightArm.SetParent(BodyRoot, false); RightArm.localPosition = new Vector3(0.43f, 1.10f, 0.08f);

            CreateEye("left_eye", new Vector3(-0.085f, 1.64f, 0.34f));
            CreateEye("right_eye", new Vector3(0.085f, 1.64f, 0.34f));
            LeftBlade = CreateBlade("left_phantom_knife", LeftArm, -1f, out LeftBladeTrail);
            RightBlade = CreateBlade("right_phantom_knife", RightArm, 1f, out RightBladeTrail);
        }

        private void CreateEye(string name, Vector3 position)
        {
            Color glow = new Color(0.72f, 0.12f, 0.95f, 0.98f);
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = name; eye.transform.SetParent(BodyRoot, false); eye.transform.localPosition = position; eye.transform.localScale = new Vector3(0.055f, 0.032f, 0.022f);
            Collider collider = eye.GetComponent<Collider>(); if (collider != null) UnityEngine.Object.Destroy(collider);
            Material material = CreateMaterial("vfx_HitSparks", glow, true);
            Renderer renderer = eye.GetComponent<Renderer>(); if (renderer != null) renderer.material = material;
            Materials.Add(material); BaseColors.Add(glow);
            if (material != null && material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", new Color(1.9f, 0.10f, 3.2f, 1f));
        }

        private LineRenderer CreateBlade(string name, Transform arm, float side, out TrailRenderer trail)
        {
            GameObject blade = new GameObject(name);
            blade.transform.SetParent(arm, false);
            blade.transform.localPosition = new Vector3(side * 0.02f, -0.53f, 0.03f);
            LineRenderer line = blade.AddComponent<LineRenderer>();
            line.material = CreateMaterial("vfx_HitSparks", new Color(0.64f, 0.18f, 0.96f, 0.96f), true);
            line.useWorldSpace = false; line.positionCount = 3; line.numCapVertices = 4; line.widthMultiplier = 0.095f;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, new Vector3(side * 0.025f, -0.36f, 0.10f));
            line.SetPosition(2, new Vector3(side * 0.09f, -0.82f, 0.18f));
            line.startColor = new Color(0.92f, 0.55f, 1f, 1f); line.endColor = new Color(0.18f, 0.025f, 0.31f, 0.10f);
            trail = blade.AddComponent<TrailRenderer>();
            trail.material = CreateMaterial("fx_perfectdodge", new Color(0.43f, 0.08f, 0.72f, 0.78f), true);
            trail.time = 0.20f; trail.minVertexDistance = 0.02f; trail.startWidth = 0.14f; trail.endWidth = 0.008f;
            trail.startColor = new Color(0.72f, 0.24f, 0.98f, 0.88f); trail.endColor = new Color(0.04f, 0.01f, 0.08f, 0f); trail.emitting = false;
            return line;
        }

        private Material CreateMaterial(string prefab, Color color, bool emissive)
        {
            Material material = MasteryVfxMaterial.CloneFromPrefab(prefab);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader != null) material = new Material(shader);
            }
            if (material == null) return null;
            if (material.HasProperty("_Color")) material.color = color;
            if (emissive && material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f, 1f));
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON"); material.EnableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
            return material;
        }

        private void BuildVfx()
        {
            GameObject motes = new GameObject("shadow_motes"); motes.transform.SetParent(transform, false); motes.transform.localPosition = Vector3.up * 0.85f;
            ShadowMotes = motes.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ShadowMotes.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.38f, 0.78f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.90f); main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.072f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.025f, 0.012f, 0.055f, 0.82f), new Color(0.48f, 0.10f, 0.72f, 0.58f)); main.maxParticles = 112;
            ParticleSystem.EmissionModule emission = ShadowMotes.emission; emission.enabled = false;
            ParticleSystem.ShapeModule shape = ShadowMotes.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.65f, 1.55f, 0.48f);
            ParticleSystemRenderer particleRenderer = ShadowMotes.GetComponent<ParticleSystemRenderer>(); particleRenderer.material = MasteryVfxMaterial.CloneFromPrefab("fx_perfectdodge"); particleRenderer.renderMode = ParticleSystemRenderMode.Stretch; particleRenderer.velocityScale = 0.42f; particleRenderer.lengthScale = 3.4f; particleRenderer.maxParticleSize = 0.085f;

            GameObject rift = new GameObject("shadow_spawn_rift"); rift.transform.SetParent(transform, false); rift.transform.localPosition = Vector3.up * 0.035f;
            GroundRift = rift.AddComponent<LineRenderer>(); GroundRift.material = MasteryVfxMaterial.CloneFromPrefab("fx_perfectdodge"); GroundRift.useWorldSpace = false; GroundRift.loop = true; GroundRift.positionCount = 40; GroundRift.widthMultiplier = 0.045f;
            for (int i = 0; i < 40; ++i) { float a = i * Mathf.PI * 2f / 40f; GroundRift.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.82f, Mathf.Sin(a * 3f) * 0.025f, Mathf.Sin(a) * 0.82f)); }

            GameObject slash = new GameObject("shadow_knife_arc"); slash.transform.SetParent(transform, false);
            SlashArc = slash.AddComponent<LineRenderer>(); SlashArc.material = MasteryVfxMaterial.CloneFromPrefab("vfx_HitSparks"); SlashArc.useWorldSpace = false; SlashArc.positionCount = 15; SlashArc.widthMultiplier = 0.075f; SlashArc.numCapVertices = 3; SlashArc.enabled = false;
            for (int i = 0; i < 15; ++i) { float a = Mathf.Lerp(-72f, 72f, i / 14f) * Mathf.Deg2Rad; SlashArc.SetPosition(i, new Vector3(Mathf.Sin(a) * 1.05f, 0.82f + Mathf.Sin(i / 14f * Mathf.PI) * 0.16f, Mathf.Cos(a) * 1.05f)); }

        }

        internal void Activate(long sourceId, Vector3 spawn, Vector3 target, int animationHash, int generation)
        {
            SourceId = sourceId; Born = Time.time; Active = true; TargetPosition = target; Generation = generation; ImpactPlayed = false; DissolvePlayed = false;
            Vector3 facing = target - spawn; facing.y = 0f;
            transform.SetPositionAndRotation(spawn, facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity);
            gameObject.SetActive(true);
            SetAlpha(0f);
            if (GroundRift != null) { GroundRift.enabled = true; GroundRift.transform.localScale = Vector3.one * 0.15f; }
            if (SlashArc != null) SlashArc.enabled = false;
            if (BodyRoot != null) { BodyRoot.localPosition = new Vector3(0f, -0.10f, -0.32f); BodyRoot.localScale = Vector3.one * 0.78f; }
            if (LeftBladeTrail != null) { LeftBladeTrail.Clear(); LeftBladeTrail.emitting = true; }
            if (RightBladeTrail != null) { RightBladeTrail.Clear(); RightBladeTrail.emitting = true; }
            if (ShadowMotes != null) { ShadowMotes.Clear(true); ShadowMotes.Emit(generation <= 1 ? 30 : 22); }
            if (ShadowAnimator != null && animationHash != 0) { ShadowAnimator.enabled = true; ShadowAnimator.speed = 0.82f; ShadowAnimator.Play(animationHash, 0, 0f); }
        }

        private void Update()
        {
            if (!Active) return;
            float age = Time.time - Born;
            float alpha = age < 0.10f ? age / 0.10f : age < 0.64f ? 1f : 1f - Mathf.Clamp01((age - 0.64f) / 0.36f);
            SetAlpha(alpha);

            float strike = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.045f, 0.185f, age));
            float recoil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.19f, 0.42f, age));
            if (BodyRoot != null)
            {
                BodyRoot.localPosition = Vector3.Lerp(new Vector3(0f, -0.10f, -0.32f), new Vector3(0f, 0.01f, 0.18f), strike) + Vector3.back * (0.10f * recoil);
                float scale = Mathf.Lerp(0.78f, 1.08f, Mathf.Sin(Mathf.Clamp01(age / 0.24f) * Mathf.PI * 0.5f));
                BodyRoot.localScale = Vector3.one * scale;
                BodyRoot.localRotation = Quaternion.Euler(Mathf.Lerp(7f, -10f, strike), Mathf.Sin(strike * Mathf.PI) * 9f, 0f);
            }
            if (LeftArm != null) LeftArm.localRotation = Quaternion.Slerp(Quaternion.Euler(8f, -20f, -58f), Quaternion.Euler(-12f, 24f, 78f), strike);
            if (RightArm != null) RightArm.localRotation = Quaternion.Slerp(Quaternion.Euler(-8f, 20f, 58f), Quaternion.Euler(12f, -24f, -78f), strike);

            if (GroundRift != null)
            {
                float ringLife = Mathf.Clamp01(age / 0.30f);
                GroundRift.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 1.38f, Mathf.SmoothStep(0f, 1f, ringLife));
                Color ringColor = new Color(0.26f, 0.33f, 0.44f, (1f - ringLife) * 0.78f); GroundRift.startColor = GroundRift.endColor = ringColor;
                GroundRift.enabled = ringLife < 1f;
            }

            if (SlashArc != null)
            {
                bool slashVisible = age >= 0.085f && age <= 0.245f;
                SlashArc.enabled = slashVisible;
                if (slashVisible)
                {
                    float t = Mathf.InverseLerp(0.085f, 0.245f, age);
                    Color edge = new Color(0.62f, 0.72f, 0.86f, Mathf.Sin(t * Mathf.PI) * 0.92f); SlashArc.startColor = edge; SlashArc.endColor = new Color(0.10f, 0.13f, 0.19f, edge.a * 0.35f);
                    SlashArc.widthMultiplier = Mathf.Lerp(0.11f, 0.025f, t);
                }
            }

            if (!ImpactPlayed && age >= 0.145f) { ImpactPlayed = true; Knife70ShadowVisualService.PlayImpact(TargetPosition, Generation); }
            if (LeftBlade != null && RightBlade != null)
            {
                float pulse = 0.75f + Mathf.Sin(Mathf.Clamp01(age / 0.24f) * Mathf.PI) * 0.45f;
                LeftBlade.widthMultiplier = RightBlade.widthMultiplier = 0.095f * pulse;
            }
            if (age >= 0.32f) { if (LeftBladeTrail != null) LeftBladeTrail.emitting = false; if (RightBladeTrail != null) RightBladeTrail.emitting = false; }
            if (!DissolvePlayed && age >= 0.62f) { DissolvePlayed = true; if (ShadowMotes != null) ShadowMotes.Emit(28); }
            if (age >= 1.02f)
            {
                Active = false;
                if (LeftBladeTrail != null) LeftBladeTrail.emitting = false;
                if (RightBladeTrail != null) RightBladeTrail.emitting = false;
                Knife70ShadowVisualService.Release(this, SourceId);
            }
        }

        private void SetAlpha(float factor)
        {
            for (int i = 0; i < Materials.Count; ++i)
            {
                Material material = Materials[i]; if (material == null || !material.HasProperty("_Color")) continue;
                Color color = BaseColors[i]; color.a *= Mathf.Clamp01(factor); material.color = color;
            }
        }
    }

    #endif

    internal sealed class Knife70ShadowEchoVisual : MonoBehaviour
    {
        private GameObject WraithVisual;
        private Animator WraithAnimator;
        private bool VisualConfigured;
        private bool GesturePlayed;
        private string NativeAttackTrigger;
        private float NextVisualRetry;
        private float Born;
        private long SourceId;
        private bool Active;
        private bool VanishPlayed;
        private int Generation;
        private Character TrackedTarget;
        private Vector3 LastTargetCenter;
        internal long OwnerId => SourceId;

        internal void Configure()
        {
            EnsureVisual();
            gameObject.SetActive(false);
        }

        private void EnsureVisual()
        {
            if (VisualConfigured || !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Time.time < NextVisualRetry) return;
            NextVisualRetry = Time.time + .10f;
            GameObject prefab = ZNetScene.instance?.GetPrefab("Wraith") ??
                NativeSoftVisualAssets.Get<GameObject>("Assets/Characters/Wraith/Wraith.prefab");
            Character character = prefab != null ? (prefab.GetComponent<Character>() ?? prefab.GetComponentInChildren<Character>(true)) : null;
            // Character.Awake assigns m_visual from this exact native child.
            // An unspawned prefab need not have the runtime field initialized.
            GameObject visualPrefab = character?.m_visual ?? prefab?.transform.Find("Visual")?.gameObject;
            if (visualPrefab != null)
            {
                VisualConfigured = true;
                // Clone only the native visual child, below an inactive staging
                // parent. No Character/AI/ZNetView or animation-event script may
                // wake up during creation, including when VFX is enabled mid-echo.
                GameObject staging = new GameObject("ValheimMastery_InactiveWraithVisual");
                staging.SetActive(false);
                try
                {
                    WraithVisual = UnityEngine.Object.Instantiate(visualPrefab, staging.transform, false);
                    WraithVisual.SetActive(false);
                    WraithVisual.name = "ValheimMastery_VanillaWraithVisual";
                    WraithAnimator = WraithVisual.GetComponentInChildren<Animator>(true);
                    foreach (Behaviour behaviour in WraithVisual.GetComponentsInChildren<Behaviour>(true))
                        if (behaviour != WraithAnimator) UnityEngine.Object.DestroyImmediate(behaviour);
                    foreach (Collider collider in WraithVisual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                    foreach (Rigidbody body in WraithVisual.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
                    foreach (ParticleSystem particles in WraithVisual.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ParticleSystem.MainModule main = particles.main;
                        main.maxParticles = Mathf.Min(main.maxParticles, 96);
                        main.stopAction = ParticleSystemStopAction.None;
                        ParticleSystem.CollisionModule collision = particles.collision; collision.enabled = false;
                        ParticleSystem.TriggerModule trigger = particles.trigger; trigger.enabled = false;
                    }
                    if (WraithAnimator != null)
                    {
                        WraithAnimator.fireEvents = false;
                        WraithAnimator.applyRootMotion = false;
                        NativeAttackTrigger = FindNativeAttackTrigger(prefab, WraithAnimator);
                    }
                    WraithVisual.transform.SetParent(transform, false);
                    WraithVisual.transform.localPosition = Vector3.zero;
                    WraithVisual.transform.localRotation = Quaternion.identity;
                    WraithVisual.transform.localScale = Vector3.one;
                }
                finally { UnityEngine.Object.Destroy(staging); }
            }
            // Null can mean async loading, not a missing donor. Warmup owns bounded diagnostics.
        }

        private static string FindNativeAttackTrigger(GameObject prefab, Animator animator)
        {
            GameObject[] items = prefab.GetComponent<Humanoid>()?.m_defaultItems;
            if (items == null) return null;
            foreach (GameObject item in items)
            {
                Attack attack = item != null ? item.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_attack : null;
                string animation = attack?.m_attackAnimation;
                if (string.IsNullOrEmpty(animation)) continue;
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                    if (parameter.type == AnimatorControllerParameterType.Trigger &&
                        (parameter.name == animation || (attack.m_attackChainLevels > 1 && parameter.name == animation + "0")))
                        return parameter.name;
            }
            return null; // Native hover remains a valid fallback; never guess a player animation.
        }

        private void StartNativeGesture()
        {
            if (GesturePlayed || WraithAnimator == null || WraithVisual == null || !WraithVisual.activeInHierarchy) return;
            GesturePlayed = true;
            WraithAnimator.enabled = true;
            WraithAnimator.fireEvents = false;
            WraithAnimator.applyRootMotion = false;
            WraithAnimator.Rebind();
            WraithAnimator.Update(0f);
            WraithAnimator.speed = Generation <= 1 ? 1.70f : 2.60f;
            if (!string.IsNullOrEmpty(NativeAttackTrigger)) WraithAnimator.SetTrigger(NativeAttackTrigger);
        }

        internal void Activate(long sourceId, Vector3 spawn, Vector3 target, int animationHash, int generation, Character tracked)
        {
            EnsureVisual();
            SourceId = sourceId;
            Born = Time.time;
            Active = true;
            GesturePlayed = false;
            VanishPlayed = false;
            Generation = generation;
            TrackedTarget = tracked;
            LastTargetCenter = tracked != null ? tracked.GetCenterPoint() : target;
            Vector3 facing = target - spawn; facing.y = 0f;
            transform.SetPositionAndRotation(spawn, facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity);
            gameObject.SetActive(true);
            if (WraithVisual != null)
            {
                WraithVisual.SetActive(MasteryPlugin.Settings.EnablePerkProcVFX.Value);
                WraithVisual.transform.localPosition = new Vector3(0f, -0.12f, -0.32f);
                WraithVisual.transform.localRotation = Quaternion.identity;
                WraithVisual.transform.localScale = Vector3.one * 0.42f;
                StartNativeGesture();
            }
            if (generation <= 1) Knife70ShadowVisualService.SpawnVanillaEffect("vfx_ShadowPerson_death", spawn + Vector3.up * .40f, .30f, .25f);
        }

        private void Update()
        {
            if (!Active) return;
            float age = Time.time - Born;
            EnsureVisual();
            float lifetime = Generation <= 1 ? .70f : .36f;
            if (TrackedTarget != null)
            {
                Vector3 center = TrackedTarget.GetCenterPoint();
                transform.position += center - LastTargetCenter;
                LastTargetCenter = center;
            }
            if (WraithVisual != null)
            {
                // Only hide the model. This controller also schedules impact audio,
                // which must remain independent of the user's VFX preference.
                WraithVisual.SetActive(MasteryPlugin.Settings.EnablePerkProcVFX.Value);
                if (age < 0.30f) StartNativeGesture();
                float arrival = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .08f));
                float lunge = Mathf.Sin(Mathf.PI * Mathf.Clamp01(age / (lifetime * .55f)));
                float departure = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lifetime * .55f, lifetime, age));
                WraithVisual.transform.localPosition = Vector3.Lerp(new Vector3(0f, -0.12f, -0.32f), new Vector3(0f, 0.02f, 0.02f), arrival) +
                    Vector3.forward * (0.38f * lunge - 0.16f * departure) + Vector3.up * (0.28f * departure);
                float scale = Mathf.Lerp(.28f, Generation <= 1 ? .78f : .55f, arrival) * Mathf.Lerp(1f, .08f, departure);
                WraithVisual.transform.localScale = Vector3.one * scale;
                WraithVisual.transform.localRotation = Quaternion.Euler(-18f * lunge, 12f * lunge, 0f);
            }
            if (!VanishPlayed && age >= lifetime * .70f)
            {
                VanishPlayed = true;
                Knife70ShadowVisualService.PlayVanish(transform.position + Vector3.up * 0.42f, Generation);
            }
            if (age >= lifetime)
            {
                Active = false;
                TrackedTarget = null;
                if (WraithVisual != null) WraithVisual.SetActive(false);
                Knife70ShadowVisualService.Release(this, SourceId);
            }
        }
    }

    internal sealed class Knife70ShadowImpact : MonoBehaviour
    {
        private LineRenderer CrossA;
        private LineRenderer CrossB;
        private LineRenderer PulseRing;
        private ParticleSystem Sparks;
        private float Born;
        private bool Configured;

        internal void Activate(Vector3 position, int generation)
        {
            if (!Configured) Configure();
            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            transform.localScale = Vector3.one * (generation <= 1 ? 1f : 0.82f);
            gameObject.SetActive(true); Born = Time.time;
            CrossA.enabled = CrossB.enabled = true;
            if (Sparks != null) { Sparks.Clear(true); Sparks.Emit(generation <= 1 ? 16 : 10); }
        }

        private void Configure()
        {
            Configured = true;
            CrossA = MakeSlash("impact_slash_a", Quaternion.Euler(0f, 0f, 28f));
            CrossB = MakeSlash("impact_slash_b", Quaternion.Euler(0f, 90f, -28f));
            GameObject ring = new GameObject("impact_shadow_ring"); ring.transform.SetParent(transform, false); ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            PulseRing = ring.AddComponent<LineRenderer>(); PulseRing.material = MasteryVfxMaterial.CloneFromPrefab("fx_perfectdodge"); PulseRing.useWorldSpace = false; PulseRing.loop = true; PulseRing.positionCount = 36; PulseRing.widthMultiplier = 0.065f;
            for (int i = 0; i < 36; ++i) { float a = i * Mathf.PI * 2f / 36f; PulseRing.SetPosition(i, new Vector3(Mathf.Cos(a) * 0.62f, Mathf.Sin(a) * 0.62f, 0f)); }
            GameObject particles = new GameObject("impact_motes"); particles.transform.SetParent(transform, false);
            Sparks = particles.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = Sparks.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = new ParticleSystem.MinMaxCurve(0.10f, 0.26f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.3f, 3.4f); main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.085f); main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.68f, 0.76f, 0.88f, 0.82f), new Color(0.09f, 0.12f, 0.18f, 0.25f)); main.maxParticles = 32;
            ParticleSystem.EmissionModule emission = Sparks.emission; emission.enabled = false;
            ParticleSystem.ShapeModule shape = Sparks.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.18f;
            ParticleSystemRenderer renderer = Sparks.GetComponent<ParticleSystemRenderer>(); renderer.material = MasteryVfxMaterial.CloneFromPrefab("vfx_HitSparks"); renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.maxParticleSize = 0.12f;
            gameObject.SetActive(false);
        }

        private LineRenderer MakeSlash(string name, Quaternion rotation)
        {
            GameObject child = new GameObject(name); child.transform.SetParent(transform, false); child.transform.localRotation = rotation;
            LineRenderer line = child.AddComponent<LineRenderer>(); line.material = MasteryVfxMaterial.CloneFromPrefab("vfx_HitSparks"); line.useWorldSpace = false; line.positionCount = 3; line.numCapVertices = 3; line.widthMultiplier = 0.075f;
            line.SetPosition(0, new Vector3(-0.72f, -0.04f, 0f)); line.SetPosition(1, new Vector3(0f, 0.05f, 0f)); line.SetPosition(2, new Vector3(0.72f, -0.04f, 0f));
            return line;
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.time - Born) / 0.24f);
            float a = (1f - t) * 0.95f;
            Color bright = new Color(0.82f, 0.32f, 1f, a); Color dark = new Color(0.10f, 0.01f, 0.18f, a * 0.24f);
            if (CrossA != null) { CrossA.startColor = bright; CrossA.endColor = dark; CrossA.widthMultiplier = Mathf.Lerp(0.10f, 0.015f, t); }
            if (CrossB != null) { CrossB.startColor = bright; CrossB.endColor = dark; CrossB.widthMultiplier = Mathf.Lerp(0.075f, 0.010f, t); }
            if (PulseRing != null)
            {
                PulseRing.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, 2.4f, Mathf.SmoothStep(0f, 1f, t));
                Color ring = new Color(0.46f, 0.07f, 0.76f, (1f - t) * 0.72f); PulseRing.startColor = PulseRing.endColor = ring;
                PulseRing.widthMultiplier = Mathf.Lerp(0.085f, 0.012f, t);
            }
            transform.localScale *= 1f + Time.deltaTime * 2.2f;
            if (t >= 1f) Knife70ShadowVisualService.Release(this);
        }
    }
    internal sealed class Knife70ShadowStreak : MonoBehaviour
    {
        private LineRenderer Line;
        private float Born;
        private const float Lifetime = 0.13f;

        internal void Activate(Vector3 from, Vector3 to, bool otherBranch)
        {
            if (Line == null)
            {
                Line = gameObject.AddComponent<LineRenderer>();
                Line.material = MasteryVfxMaterial.CloneFromPrefab("fx_perfectdodge");
                Line.useWorldSpace = true; Line.positionCount = 3; Line.numCapVertices = 3;
            }
            gameObject.SetActive(true); Born = Time.time;
            Vector3 middle = Vector3.Lerp(from, to, 0.5f) + Vector3.up * 0.22f;
            Line.SetPosition(0, from); Line.SetPosition(1, middle); Line.SetPosition(2, to);
            Line.widthMultiplier = otherBranch ? 0.055f : 0.042f;
        }

        private void Update()
        {
            float t = Mathf.Clamp01((Time.time - Born) / Lifetime);
            if (Line != null) { Color c = new Color(0.16f, 0.19f, 0.24f, (1f - t) * 0.62f); Line.startColor = Line.endColor = c; }
            if (t >= 1f) Knife70ShadowVisualService.Release(this);
        }
    }

    internal static class Knife70DebugOverlay
    {
        private static GameObject Root;
        private static TextMeshProUGUI Text;

        internal static void Set(string value, bool visible)
        {
            if (!visible) { if (Root != null) Root.SetActive(false); return; }
            Ensure();
            if (Root == null || Text == null) return;
            Root.SetActive(true); Text.text = "<b>KNIFE70 SHADOW</b>\n" + value;
        }

        private static void Ensure()
        {
            if (Root != null || ZNet.instance == null || ZNet.instance.IsServer() && Player.m_localPlayer == null) return;
            Root = new GameObject("ValheimMastery_Knife70DebugOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            Canvas canvas = Root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 250;
            CanvasScaler scaler = Root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
            GameObject label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(Root.transform, false);
            RectTransform rect = label.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(1f, 1f); rect.anchoredPosition = new Vector2(-22f, -90f); rect.sizeDelta = new Vector2(620f, 180f);
            Text = label.GetComponent<TextMeshProUGUI>(); Text.fontSize = 20f; Text.alignment = TextAlignmentOptions.TopRight; Text.color = new Color(0.74f, 0.82f, 0.91f, 0.95f); Text.raycastTarget = false;
            TextMeshProUGUI vanilla = MessageHud.instance?.GetComponentInChildren<TextMeshProUGUI>(true); if (vanilla != null) Text.font = vanilla.font;
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class Knife70WorldCleanupPatch
    {
        private static void Prefix() => Knife70ShadowStrikeService.Reset();
    }
}
