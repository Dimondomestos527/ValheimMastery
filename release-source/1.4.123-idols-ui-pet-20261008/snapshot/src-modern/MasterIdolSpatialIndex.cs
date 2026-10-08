using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Horizontal broad phase only; callers retain exact native distance checks.
    internal sealed class MasterIdolSpatialIndex
    {
        private const double CellSize = 32d;
        private sealed class Entry { internal Vector3 Point; internal float Radius; internal List<long> Cells; }
        private readonly Dictionary<ZDOID, Entry> Entries = new Dictionary<ZDOID, Entry>();
        private readonly Dictionary<long, HashSet<ZDOID>> Cells = new Dictionary<long, HashSet<ZDOID>>();
        private readonly HashSet<ZDOID> Overflow = new HashSet<ZDOID>();
        private static int Cell(float value) => (int)Math.Floor(value / CellSize);
        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
        internal void Clear() { Entries.Clear(); Cells.Clear(); Overflow.Clear(); }
        internal bool Location(ZDOID id, out Vector3 point, out float radius)
        { if (Entries.TryGetValue(id, out var e)) { point=e.Point; radius=e.Radius; return true; } point=default; radius=0; return false; }
        internal void Remove(ZDOID id)
        {
            if (!Entries.TryGetValue(id,out var e)) return;
            if(e.Cells!=null) foreach(long cell in e.Cells) if(Cells.TryGetValue(cell,out var ids)) { ids.Remove(id); if(ids.Count==0)Cells.Remove(cell); }
            Entries.Remove(id); Overflow.Remove(id);
        }
        internal void Set(ZDOID id, Vector3 point, float radius)
        {
            Remove(id); var entry=new Entry { Point=point, Radius=Math.Max(0,radius) }; Entries[id]=entry;
            double minX=Math.Floor((point.x-entry.Radius)/CellSize),maxX=Math.Floor((point.x+entry.Radius)/CellSize);
            double minZ=Math.Floor((point.z-entry.Radius)/CellSize),maxZ=Math.Floor((point.z+entry.Radius)/CellSize);
            // Unusual modded ranges are still included, without allocating millions of cells.
            if(double.IsNaN(minX)||double.IsNaN(minZ)||minX<int.MinValue||maxX>=int.MaxValue||minZ<int.MinValue||maxZ>=int.MaxValue||
                (maxX-minX+1)*(maxZ-minZ+1)>4096) { Overflow.Add(id); return; }
            entry.Cells=new List<long>();
            for(int x=(int)minX;x<=maxX;x++)for(int z=(int)minZ;z<=maxZ;z++)
            { long key=Key(x,z); if(!Cells.TryGetValue(key,out var ids)){ ids=new HashSet<ZDOID>();Cells[key]=ids; } ids.Add(id);entry.Cells.Add(key); }
        }
        internal readonly struct CandidateView
        {
            private readonly HashSet<ZDOID> Cell,Overflow;internal CandidateView(HashSet<ZDOID> cell,HashSet<ZDOID> overflow){Cell=cell;Overflow=overflow;}
            public Enumerator GetEnumerator()=>new Enumerator(Cell,Overflow);
            internal struct Enumerator:IDisposable
            {
                private HashSet<ZDOID>.Enumerator Cell,Overflow;private bool InCell;private ZDOID Value;
                internal Enumerator(HashSet<ZDOID> cell,HashSet<ZDOID> overflow){Cell=cell!=null?cell.GetEnumerator():default;Overflow=overflow.GetEnumerator();InCell=cell!=null;Value=default;}
                public ZDOID Current=>Value;public bool MoveNext(){if(InCell){if(Cell.MoveNext()){Value=Cell.Current;return true;}InCell=false;}if(Overflow.MoveNext()){Value=Overflow.Current;return true;}return false;}public void Dispose(){Cell.Dispose();Overflow.Dispose();}
            }
        }
        internal CandidateView At(Vector3 point)
        {Cells.TryGetValue(Key(Cell(point.x),Cell(point.z)),out var ids);return new CandidateView(ids,Overflow);}
        internal HashSet<ZDOID> Near(Vector3 point,float radius)
        {var result=new HashSet<ZDOID>();FillNear(point,radius,result);return result;}
        internal void FillNear(Vector3 point,float radius,HashSet<ZDOID> result,bool clear=true)
        {
            if(clear)result.Clear();result.UnionWith(Overflow);
            double minX=Math.Floor((point.x-radius)/CellSize),maxX=Math.Floor((point.x+radius)/CellSize);
            double minZ=Math.Floor((point.z-radius)/CellSize),maxZ=Math.Floor((point.z+radius)/CellSize);
            if(double.IsNaN(minX)||double.IsNaN(minZ)||minX<int.MinValue||maxX>=int.MaxValue||minZ<int.MinValue||maxZ>=int.MaxValue||(maxX-minX+1)*(maxZ-minZ+1)>4096)
            { foreach(var id in Entries.Keys)result.Add(id); return; }
            for(int x=(int)minX;x<=maxX;x++)for(int z=(int)minZ;z<=maxZ;z++)
                if(Cells.TryGetValue(Key(x,z),out var ids))foreach(var id in ids)result.Add(id);

        }
    }
}
