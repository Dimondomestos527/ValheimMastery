using Mono.Cecil;

internal static class Patch122Checks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition M(string type, string method) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method);
        bool Calls(string type, string method, string owner, string name) => M(type, method).Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.Name == owner && r.Name == name);
        bool Text(string type, string method, string value) => M(type, method).Body.Instructions.Any(i => i.Operand is string s && s == value);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        Check(Calls("Skeleton35PortalPatch", "Prefix", "Skeleton35Travel", "PrepareForPortal") && Calls("Skeleton35Travel", "PrepareForPortal", "Skeleton35Travel", "PublishPersistentOwnedFollower") && Calls("Skeleton35Travel", "PublishPersistentOwnedFollower", "ZDO", "set_Persistent"), "owned living summons become persistent before native portal unload");
        Check(Calls("Skeleton35Travel", "TagSpawnedSummon", "Skeleton35Travel", "PublishPersistentOwnedFollower") && Calls("Skeleton35Travel", "PublishPersistentOwnedFollower", "ZDOMan", "ForceSendZDO"), "owner birth persistence is revisioned and force-sent before sector eviction");
        Check(Calls("SummonRosterCommands", "Tick", "Time", "get_time") && !Calls("SummonRosterCommands", "Tick", "Time", "get_deltaTime"), "dismiss hold uses actual elapsed time after acknowledgement, never a pre-ACK frame delta");
        Check(Calls("SummonRosterCommands", "Receive", "ZDO", "GetByteArray") && Calls("SummonRosterCommands", "Receive", "ZDO", "SetPosition"), "portal keeps and audits the same independent cargo record");
        Check(Calls("Magic70Carrier", "OpenFromCarrier", "Container", "Load") && !Calls("Magic70Carrier", "OpenFromCarrier", "Container", "Save"), "cargo opening reads canonical inventory before display without rewriting items");
        Check(Text("Magic70Carrier", "RegisterCarrier", "Торба") && Text("Magic70Carrier", "RegisterCargo", "Шлунок Торби"), "carrier and cargo names updated without changing persistent prefab identities");
        Check(Calls("Magic70Carrier", "RegisterCargoInventory", "Inventory", "GetName") && Calls("Magic70Carrier", "RegisterCargoInventory", "FieldInfo", "SetValue"), "saved cargo inventory name migrates through the actual native UI title field");
        Check(Calls("CarrierPackVisual", "RemoveHand", "Humanoid", "UnequipItem") && Calls("CarrierPackVisual", "RemoveHand", "Inventory", "RemoveItem"), "owner removes only carrier-body hand equipment, not cargo");
        Check(!Calls("CarrierPackVisual", "Update", "Object", "Instantiate") && !Calls("CarrierPackVisual", "Update", "ZDOMan", "CreateNewZDO"), "decorative pack copies meshes only, never a chest gameplay prefab or extra inventory");
        Console.WriteLine(count + " patch122 STATIC checks passed; multiplayer, cargo persistence and visual appearance need live verification.");
        return 0;
    }
}
