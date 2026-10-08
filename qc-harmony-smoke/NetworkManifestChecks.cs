using System.Globalization;
using System.Reflection;

internal static class NetworkManifestChecks
{
    internal static int Run(Assembly plugin)
    {
        var runtime = plugin.GetType("ValheimMastery.MasteryRuntime", true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var names = new[] { "TierStep", "FirstUniqueMultiplier", "DodgeXpCoefficient",
            "WorldBossXpScalingEnabled", "EarlyBossXpBonus", "LateBossXpBonus",
            "LegacyCatchUpEnabled", "LegacyCatchUpScale", "CombatDamageSkillScalingDefault",
            "CombatDamageSkillScalingBow", "CombatDamageSkillScalingClub", "ClubStaggerPerSkillLevel" };
        object[] expected = { .3f, 10f, 1.75f, true, .05f, .1f, false, 2.5f, .67f, .4f, .55f, .005f };
        var previous = names.Select(name => runtime.GetProperty(name, flags).GetValue(null)).ToArray();
        var culture = CultureInfo.CurrentCulture;
        try
        {
            for (int i = 0; i < names.Length; i++) runtime.GetProperty(names[i], flags).SetValue(null, expected[i]);
            foreach (var locale in new[] { "en-US", "uk-UA", "de-DE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                var fields = ((string)runtime.GetMethod("Manifest", flags).Invoke(null, null)).Split('|');
                if (fields.Length != 13) throw new Exception($"Server manifest has {fields.Length} fields; client expects 13.");
                var version = (string)plugin.GetType("ValheimMastery.MasteryPlugin", true).GetField("Version", BindingFlags.Public | flags).GetRawConstantValue();
                if (fields[0] != version) throw new Exception("Manifest version mismatch.");
                for (int i = 0; i < expected.Length; i++)
                {
                    if (expected[i] is bool flag)
                    {
                        if (!bool.TryParse(fields[i + 1], out var actual) || actual != flag) throw new Exception($"Invalid {names[i]} in {locale}.");
                    }
                    else if (!float.TryParse(fields[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) || actual != (float)expected[i])
                        throw new Exception($"Wrong network value/order: {names[i]} in {locale}.");
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            for (int i = 0; i < names.Length; i++) runtime.GetProperty(names[i], flags).SetValue(null, previous[i]);
        }
        Console.WriteLine("PASS: actual server manifest decodes all 13 client fields, including dodge XP, in 3 locales. Live handshake/craft/build still required.");
        return 0;
    }
}
