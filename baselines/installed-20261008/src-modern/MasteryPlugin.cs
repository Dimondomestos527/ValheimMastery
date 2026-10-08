using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ValheimMastery
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class MasteryPlugin : BaseUnityPlugin
    {
        public const string Guid = "domestos.valheim.mastery";
        public const string Name = "Valheim Mastery";
        public const string Version = "1.4.123";

        internal static ManualLogSource Log;
        internal static MasteryPlugin Instance;
        internal static MasteryConfig Settings;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            try
            {
                Log = Logger;
                Settings = new MasteryConfig(base.Config);
                GoldCraftingService.Initialize(base.Config);
                PantheonActiveInput.Initialize(base.Config);
                // Extended catalog must be enabled BEFORE the game's first SoftRef load.
                // This indexes assets; it does not load every bundle or instantiate prefabs.
                if (!UnityEngine.Application.isBatchMode) SoftReferenceableAssets.Runtime.MakeAllAssetsLoadable();
                MasteryRuntime.ResetToLocalConfig();
                _harmony = new Harmony(Guid);
                _harmony.PatchAll();
                Log.LogInfo("Harmony patch registration completed successfully.");
                PerkDebugService.RegisterCommands();
                PantheonActiveInput.FreezeRegistration();
                Log.LogInfo(Name + " " + Version + " loaded. World boss XP scaling and 24-skill / 72-entry catalog loaded; catalog entries do not by themselves confirm implemented gameplay perks.");
            }
            catch (System.Exception error)
            {
                Log?.LogError("Mastery initialization failed; removing partial services and patches. " + error);
                try { CleanupRuntime(); }
                catch (System.Exception cleanupError) { Log?.LogError("Mastery cleanup failed: " + cleanupError); }
                throw;
            }
        }

        private void Update()
        {
            GoldCraftingService.Tick();
            PantheonActiveInput.Tick();
            Magic70Carrier.Tick();
            NetworkSync.Tick(); OwnerSkillAuthority.Tick(); WorkshopRemoteCraft.Tick(); WorkshopRecovery.Tick(); WorkshopChestLease.Tick();
#if MASTERY_MAGIC70_EXPERIMENT
            Magic70Surtling.Tick();
#endif
#if MASTERY_SPEAR35_EXPERIMENT
            Spear70HookService.Tick();
#endif
            WeaponGlowService.Tick(); Axes70Service.Tick(); FarmingPerkService.Tick(); CookingLegacyMigrationService.Tick(); WoodCuttingChainService.Tick(); RuntimeSelfTest.Tick();
        }
        private void OnDestroy() { CleanupRuntime(); }

        private void CleanupRuntime()
        {
            enabled = false;
            try { PantheonActiveInput.RequestShutdown(); }
            finally
            {
                try { GoldCraftingService.Shutdown(); }
                finally
                {
                    if (Instance == this) Instance = null;
                    _harmony?.UnpatchSelf();
                }
            }
        }
    }

    internal sealed class MasteryConfig
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<bool> EnforceMatchingMod;
        internal readonly ConfigEntry<bool> VerboseLogging;
        internal readonly ConfigEntry<bool> WorkshopTiming;
        internal readonly ConfigEntry<bool> ExperimentalOptimization;
        internal readonly ConfigEntry<float> TierStep;
        internal readonly ConfigEntry<float> FirstUniqueMultiplier;
        internal readonly ConfigEntry<float> JumpBaseXp;
        internal readonly ConfigEntry<float> DodgeXpCoefficient;
        internal readonly ConfigEntry<float> HandshakeTimeoutSeconds;
        internal readonly ConfigEntry<bool> EnableWorldBossXpScaling;
        internal readonly ConfigEntry<float> EarlyBossXpBonus;
        internal readonly ConfigEntry<float> LateBossXpBonus;
        internal readonly ConfigEntry<bool> EnableMasterySkillUI;
        internal readonly ConfigEntry<bool> ShowMilestoneMarkers;
        internal readonly ConfigEntry<bool> ShowFlavorTextOnShift;
        internal readonly ConfigEntry<bool> ShowNextMilestoneProgress;
        internal readonly ConfigEntry<bool> EnableMilestoneVFX;
        internal readonly ConfigEntry<bool> EnableMilestoneMessages;
        internal readonly ConfigEntry<bool> EnablePerkProcVFX;
        internal readonly ConfigEntry<bool> EnablePerkProcMessages;
        internal readonly ConfigEntry<bool> EnablePerkSFX;
        internal readonly ConfigEntry<float> MasteryTooltipWidth;
        internal readonly ConfigEntry<bool> UIDebugLogging;
        internal readonly ConfigEntry<float> Blocking35MinTargetStagger;
        internal readonly ConfigEntry<float> Blocking35MaxTargetStagger;
        internal readonly ConfigEntry<float> Blocking35MinExtraAdrenaline;
        internal readonly ConfigEntry<float> Blocking35MaxExtraAdrenaline;
        internal readonly ConfigEntry<bool> EnableLegacyCatchUp;
        internal readonly ConfigEntry<float> LegacyCatchUpScale;
        internal readonly ConfigEntry<float> CombatDamageSkillScalingDefault;
        internal readonly ConfigEntry<float> CombatDamageSkillScalingBow;
        internal readonly ConfigEntry<float> CombatDamageSkillScalingClub;
        internal readonly ConfigEntry<float> ClubStaggerPerSkillLevel;

        internal MasteryConfig(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true, "Enable Valheim Mastery.");
            EnforceMatchingMod = config.Bind("Network", "EnforceMatchingMod", false, "Disconnect clients that do not complete the Mastery version handshake.");
            VerboseLogging = config.Bind("Debug", "VerboseLogging", false, "Log XP formula components and handshake details.");
            WorkshopTiming = config.Bind("Debug", "WorkshopTiming", false, "Opt-in bounded workshop stage timings and diagnostic RTT echo. Does not change resource transactions. Enable on client and server for profiling, then disable.");
            ExperimentalOptimization = config.Bind("Performance", "ExperimentalOptimization", false, "Enable the experimental process-local Pilot A optimization. Configure a dedicated server separately.");
            TierStep = config.Bind("Progression", "TierStep", 0.30f, "Tier coefficient. Formula: BaseXP * BossXPModifier * (1 + coefficient*(target-1) + coefficient*(item-1)).");
            FirstUniqueMultiplier = config.Bind("Progression", "FirstUniqueMultiplier", 10f, "Multiplier applied after boss and tier multipliers for the first unique action.");
            JumpBaseXp = config.Bind("Progression", "JumpBaseXp", 0.25f, "Base XP awarded for a successful jump.");
            DodgeXpCoefficient = config.Bind("Progression", "DodgeXpCoefficient", 5.0f, "Server-synchronized multiplier applied to Dodge XP after world progression and before first-time bonus.");
            EnableWorldBossXpScaling = config.Bind("WorldProgression", "EnableWorldBossXpScaling", true, "Scale BaseXP from vanilla defeated-boss world keys.");
            EarlyBossXpBonus = config.Bind("WorldProgression", "EarlyBossXpBonus", 0.05f, "Additive BaseXP bonus for each defeated early boss through Yagluth.");
            LateBossXpBonus = config.Bind("WorldProgression", "LateBossXpBonus", 0.10f, "Additive BaseXP bonus for each defeated late boss (Queen and Fader).");
            HandshakeTimeoutSeconds = config.Bind("Network", "HandshakeTimeoutSeconds", 8f, "Seconds allowed for a client to confirm its mod version and server settings.");
            EnableMasterySkillUI = config.Bind("UI", "EnableMasterySkillUI", false, "Show Mastery perks in the vanilla Skills window.");
            ShowMilestoneMarkers = config.Bind("UI", "ShowMilestoneMarkers", true, "Show 35/70/100 markers when the marker overlay is available.");
            ShowFlavorTextOnShift = config.Bind("UI", "ShowFlavorTextOnShift", true, "Show perk flavor text while Left Shift is held (mastery-card phase).");
            ShowNextMilestoneProgress = config.Bind("UI", "ShowNextMilestoneProgress", true, "Show levels remaining until the next perk.");
            EnableMilestoneVFX = config.Bind("UI", "EnableMilestoneVFX", true, "Play the vanilla skill-level-up effect for first-time milestone unlocks.");
            EnableMilestoneMessages = config.Bind("UI", "EnableMilestoneMessages", true, "Show a center-screen message for first-time milestone unlocks.");
            EnablePerkProcVFX = config.Bind("UI", "EnablePerkProcVFX", true, "Play short vanilla VFX when chance/active perks proc.");
            EnablePerkProcMessages = config.Bind("UI", "EnablePerkProcMessages", true, "Show compact text notifications for selected perk procs.");
            EnablePerkSFX = config.Bind("UI", "EnablePerkSFX", true, "Play themed vanilla sound effects for perk procs.");
            MasteryTooltipWidth = config.Bind("UI", "MasteryTooltipWidth", 430f, "Preferred width of the side mastery card in pixels.");
            UIDebugLogging = config.Bind("UI", "DebugLogging", false, "Log the SkillsDialog hierarchy and row mapping once.");
            Blocking35MinTargetStagger = config.Bind("Perks", "Blocking35MinTargetStagger", 0.10f, "Minimum added target stagger fraction on a level-35 perfect parry.");
            Blocking35MaxTargetStagger = config.Bind("Perks", "Blocking35MaxTargetStagger", 0.40f, "Maximum added target stagger fraction on a level-35 perfect parry.");
            Blocking35MinExtraAdrenaline = config.Bind("Perks", "Blocking35MinExtraAdrenaline", 2f, "Minimum extra adrenaline on a level-35 perfect parry.");
            Blocking35MaxExtraAdrenaline = config.Bind("Perks", "Blocking35MaxExtraAdrenaline", 8f, "Maximum extra adrenaline on a level-35 perfect parry.");
            EnableLegacyCatchUp = config.Bind("Progression", "EnableLegacyCatchUp", true, "Apply one-time XP restoration for eligible skills in progressed worlds.");
            LegacyCatchUpScale = config.Bind("Progression", "LegacyCatchUpScale", 1f, "Global scale for one-time legacy XP restoration.");
            CombatDamageSkillScalingDefault = config.Bind("CombatScaling", "CombatDamageSkillScaling_Default", 0.67f, "Keep this fraction of vanilla skill-level damage bonus for standard combat skills.");
            CombatDamageSkillScalingBow = config.Bind("CombatScaling", "CombatDamageSkillScaling_Bow", 0.55f, "Legacy Bow skill-bonus fraction before the additional 0.15 rebalance reduction (0.55 here = 0.40 effective). Does not reduce total bow damage.");
            CombatDamageSkillScalingClub = config.Bind("CombatScaling", "CombatDamageSkillScaling_Club", 0.55f, "Keep this fraction of vanilla Club skill-level damage bonus.");
            ClubStaggerPerSkillLevel = config.Bind("CombatScaling", "ClubStaggerPerSkillLevel", 0.005f, "Additive Club stagger bonus per Club skill level.");
        }
    }
}
















