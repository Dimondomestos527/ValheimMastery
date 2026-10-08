using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // Pure encounter policy. The authenticated coordinator supplies effective damage,
    // eligible skill snapshots, real hostile-action identities and terminal evidence.
    // This class NEVER authorizes Gold or mutates a wallet.
    internal sealed class Combat100FavorModel
    {
        internal const int MaxTargets = 128;
        internal const int MaxParticipants = 32;
        internal const int MaxDefenseActions = 128;
        internal const int MaxDamageReceipts = 4096;
        internal const double QuietSeconds = 12d;
        internal enum Patron { Tyr = 1, Odin = 2 }
        internal enum Threat { Ordinary = 1, Hard = 2, Elite = 3 }

        internal sealed class Reward
        {
            internal long PlayerId;
            internal string EncounterId;
            internal float Tyr, Odin;
            internal float Total => Tyr + Odin;
        }

        private sealed class Target
        {
            internal double MaxHealth, Damage;
            internal bool Resolved;
            internal Threat Band;
        }
        private sealed class Participant
        {
            internal double TyrDamage, OdinDamage, TyrDefense, OdinDefense;
            internal int DefenseCount;
        }

        private readonly Dictionary<string, Target> _targets = new Dictionary<string, Target>(StringComparer.Ordinal);
        private readonly Dictionary<long, Participant> _people = new Dictionary<long, Participant>();
        private readonly HashSet<string> _defenses = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _damageReceipts = new HashSet<string>(StringComparer.Ordinal);
        private Reward[] _completion;
        private double _lastObservedTime = -1d;
        private readonly string _id;
        internal string EncounterId => _id;
        private double _lastAction = -1d;
        private bool _closed, _cancelled, _membershipClosed;

        internal Combat100FavorModel(string encounterId)
        {
            if (!Identity(encounterId)) throw new ArgumentException("Invalid encounter identity.", nameof(encounterId));
            _id = encounterId;
        }

        internal bool AddTarget(string identity, Threat threat, double maxHealth, bool genuineHostile, double now)
        {
            if (_closed || _cancelled || _membershipClosed || !genuineHostile || !Identity(identity) || !Time(now) ||
                !Finite(maxHealth) || maxHealth <= 0d || maxHealth > 10000000d ||
                threat < Threat.Ordinary || threat > Threat.Elite) return false;
            if (_targets.TryGetValue(identity, out Target known))
                return known.Band == threat && known.MaxHealth == maxHealth;
            if (_targets.Count >= MaxTargets) return false;
            _targets.Add(identity, new Target { Band = threat, MaxHealth = maxHealth });
            Touch(now);
            return true;
        }

        // Clamp to actual current health supplied AFTER native mitigation, not raw HitData damage.
        // Healing must not replenish a target's lifetime contribution budget.
        internal bool Damage(string targetId, string receipt, long player, Patron patron, double healthBefore,
            double healthAfter, bool eligible, bool generated, double now)
        {
            if (!Identity(receipt) || _damageReceipts.Contains(receipt) || _damageReceipts.Count >= MaxDamageReceipts ||
                !CanObserve(targetId, player, patron, eligible, generated, now, out Target target) ||
                !Finite(healthBefore) || !Finite(healthAfter) || healthBefore < 0d || healthAfter < 0d ||
                healthBefore > target.MaxHealth) return false;
            double amount = Math.Min(Math.Max(0d, healthBefore - healthAfter), target.MaxHealth - target.Damage);
            if (amount <= 0d) return false;
            if (!TryPerson(player, out Participant person)) return false;
            _damageReceipts.Add(receipt);
            target.Damage += amount;
            if (patron == Patron.Tyr) person.TyrDamage += amount; else person.OdinDamage += amount;
            Touch(now);
            return true;
        }

        // A defense contributes eligibility/routing, NEVER an immediate Favor payout.
        // One originating hostile action can support only one participant/patron credit.
        // The coordinator must only submit audited block/control actions, not taken damage.
        internal bool Defense(string targetId, string hostileActionId, long player, Patron patron,
            bool eligible, bool generated, double now)
        {
            if (!Identity(hostileActionId) || _defenses.Contains(hostileActionId) ||
                _defenses.Count >= MaxDefenseActions ||
                !CanObserve(targetId, player, patron, eligible, generated, now, out Target target) ||
                !TryPerson(player, out Participant person)) return false;
            _defenses.Add(hostileActionId);
            // Bounded routing credit: additional counters do not grow beyond 10% of
            // all target health. Exact calibration remains a gameplay tuning parameter.
            double credit = target.MaxHealth * .025d;
            if (patron == Patron.Tyr) person.TyrDefense += credit; else person.OdinDefense += credit;
            person.DefenseCount++;
            Touch(now);
            return true;
        }

        internal bool Resolve(string targetId, double now)
        {
            if (_closed || _cancelled || !Time(now) || !_targets.TryGetValue(targetId ?? "", out Target target) ||
                target.Resolved) return false;
            target.Resolved = true;
            Touch(now);
            return true;
        }

        // Authoritative grouping coordinator freezes membership when the hostile episode ends.
        // It must also enforce shared spawner/chain-pull budgets; local timers alone cannot.
        internal bool SealMembership(double now)
        {
            if (_closed || _cancelled || _targets.Count == 0 || !Time(now)) return false;
            foreach (Target target in _targets.Values) if (!target.Resolved) return false;
            _membershipClosed = true;
            return true;
        }

        internal void Cancel() { _cancelled = true; }

        internal bool TryComplete(double now, out Reward[] rewards)
        {
            rewards = Array.Empty<Reward>();
            if (!Time(now)) return false;
            if (_closed) { rewards = CopyRewards(_completion); return true; }
            if (_cancelled || !_membershipClosed || now - _lastAction < QuietSeconds) return false;
            double health = 0d;
            Threat threat = Threat.Ordinary;
            foreach (Target target in _targets.Values)
            {
                if (!target.Resolved) return false;
                health += target.MaxHealth;
                if (target.Band > threat) threat = target.Band;
            }
            if (!Finite(health) || health <= 0d) return false;
            float bounty = threat == Threat.Elite ? 60f : threat == Threat.Hard ? 40f : 20f;
            var output = new List<Reward>();
            foreach (var entry in _people)
            {
                Participant p = entry.Value;
                double damage = p.TyrDamage + p.OdinDamage;
                // One scratch does not qualify; two independently authenticated defensive actions
                // may qualify a defender even without a last hit. No proximity participation.
                if (damage < Math.Max(2d, health * .05d) && p.DefenseCount < 2) continue;
                double defense = p.TyrDefense + p.OdinDefense;
                double factor = defense > 0d ? Math.Min(1d, health * .10d / defense) : 0d;
                double tyr = p.TyrDamage + p.TyrDefense * factor;
                double odin = p.OdinDamage + p.OdinDefense * factor;
                double total = tyr + odin;
                if (!Finite(total) || total <= 0d) continue;
                float tyrReward = (float)(bounty * tyr / total);
                output.Add(new Reward { PlayerId = entry.Key, EncounterId = _id,
                    Tyr = tyrReward, Odin = bounty - tyrReward });
            }
            _closed = true;
            _completion = output.ToArray();
            rewards = CopyRewards(_completion);
            return true;
        }

        private bool CanObserve(string identity, long player, Patron patron, bool eligible, bool generated,
            double now, out Target target)
        {
            target = null;
            return !_closed && !_cancelled && !_membershipClosed && player != 0 && eligible && !generated &&
                (patron == Patron.Tyr || patron == Patron.Odin) && Time(now) &&
                _targets.TryGetValue(identity ?? "", out target) && !target.Resolved;
        }
        private bool TryPerson(long player, out Participant person)
        {
            if (_people.TryGetValue(player, out person)) return true;
            if (_people.Count >= MaxParticipants) return false;
            _people.Add(player, person = new Participant());
            return true;
        }
        private bool Time(double now)
        {
            if (!Finite(now) || now < 0d || now < _lastObservedTime) return false;
            _lastObservedTime = now;
            return true;
        }
        private static Reward[] CopyRewards(Reward[] source)
        {
            var result = new Reward[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = new Reward { PlayerId = source[i].PlayerId, EncounterId = source[i].EncounterId,
                    Tyr = source[i].Tyr, Odin = source[i].Odin };
            return result;
        }
        private void Touch(double now) { _lastAction = now; }
        private static bool Finite(double number) => !double.IsNaN(number) && !double.IsInfinity(number);
        private static bool Identity(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
            foreach (char c in value) if (char.IsControl(c)) return false;
            return true;
        }
    }
}

