using UnityEngine;
namespace ValheimMastery
{
    // Immutable presentation identity; authority, token and rotation remain in transport.
    internal sealed class WorkbenchTravelDestination
    {
        internal WorkbenchTravelDestination(ZDOID id,Vector3 position,string enchantmentId,int ordinal)
        {Id=id;Position=position;EnchantmentId=enchantmentId;Ordinal=ordinal;}
        internal ZDOID Id {get;}
        internal Vector3 Position {get;}
        internal string EnchantmentId {get;}
        internal int Ordinal {get;}
    }
}
