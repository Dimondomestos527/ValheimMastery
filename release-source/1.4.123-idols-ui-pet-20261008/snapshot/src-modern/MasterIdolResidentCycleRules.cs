namespace ValheimMastery{internal static class MasterIdolResidentCycleRules
    {
        internal static float ActivityRadius(bool dormant)=>dormant?64f:80f;
        internal static bool Role(string family,bool resin)=>family=="Greydwarf_Shaman"||family=="Greyling"&&resin;
        internal static bool Refill(float fuel,float max)=>!float.IsNaN(fuel)&&!float.IsInfinity(fuel)&&!float.IsNaN(max)&&!float.IsInfinity(max)&&max>0&&fuel>=0&&fuel<max&&max-fuel>=System.Math.Min(1f,max*.25f);
    }
    }


