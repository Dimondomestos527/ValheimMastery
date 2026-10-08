using System.Reflection;

// These fixtures exercise only the managed identity migration. No scene,
// GameObject, native Unity method or player save file is created or touched.
internal static class MasterFeastPersistenceChecks
{
    private const BindingFlags Methods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(Assembly plugin)
    {
        Type migrationType = plugin.GetType("ValheimMastery.FeastFoodIdentityMigration", true)!;
        MethodInfo observe = migrationType.GetMethod("Observe", Methods)!;
        MethodInfo resolve = migrationType.GetMethod("Resolve", Methods)!;
        int passed = 0;

        Check("canonical food prefab survives", "FeastMeadows", "FeastMeadows",
            ("FeastMeadows", "$item_feast_meadows", 120f));
        Check("legacy localization key migrates", "$item_feast_meadows", "FeastMeadows",
            ("FeastMeadows", "$item_feast_meadows", 120f));
        Check("unrelated food cannot restore aura", "$item_feast_meadows", "$item_feast_meadows",
            ("CarrotSoup", "$item_carrotsoup", 120f));
        Check("ambiguous names are not guessed", "$item_feast", "$item_feast",
            ("FeastA", "$item_feast", 120f), ("FeastB", "$item_feast", 120f));
        Check("expired food cannot restore aura", "$item_feast_meadows", "$item_feast_meadows",
            ("FeastMeadows", "$item_feast_meadows", 0f));
        Check("canonical identity wins over ambiguous display names", "FeastMeadows", "FeastMeadows",
            ("OtherA", "FeastMeadows", 120f), ("OtherB", "FeastMeadows", 120f), ("FeastMeadows", "$item_feast_meadows", 120f));

        Console.WriteLine($"PASS: Master Feast persisted-food identity {passed}/6; relog/HUD/remote-author testing still requires Valheim.");
        return 0;

        void Check(string name, string saved, string expected, params (string Prefab, string Name, float Remaining)[] foods)
        {
            object migration = Activator.CreateInstance(migrationType, Methods, null, new object[] { saved }, null)!;
            foreach (var row in foods)
                observe.Invoke(migration, new object[] { row.Prefab, row.Name, row.Remaining });
            string actual = (string)resolve.Invoke(migration, null)!;
            if (actual != expected)
                throw new Exception($"Master Feast identity regression: {name}; got {actual}, expected {expected}.");
            passed++;
        }
    }
}
