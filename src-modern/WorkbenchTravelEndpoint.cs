using UnityEngine;
namespace ValheimMastery
{
    // Server-only proof snapshot; separate from map's presentation DTO.
    internal sealed class WorkbenchTravelEndpoint
    {
        internal WorkbenchTravelEndpoint(ZDOID id,string station,string choice,Vector3 point,Quaternion rotation)
        {Id=id;Station=station;Choice=choice;Point=point;Rotation=rotation;}
        internal ZDOID Id {get;}
        internal string Station {get;}
        internal string Choice {get;}
        internal Vector3 Point {get;}
        internal Quaternion Rotation {get;}
    }
}
