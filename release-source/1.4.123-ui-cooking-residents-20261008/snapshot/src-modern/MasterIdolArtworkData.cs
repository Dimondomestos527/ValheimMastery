using System;
using System.IO;
using System.Text;

namespace ValheimMastery
{
    // Independent of Unity: malformed embedded geometry cannot allocate unbounded memory.
    internal sealed class MasterIdolArtworkData
    {
        internal sealed class Part
        {
            internal string Name, Material, Prefab;
            internal byte Role;
            internal float[] Vertices;
        }
        internal Part[] Parts;
        private static string Text(BinaryReader r)
        {
            int n = r.ReadUInt16();
            if (n > 4096) throw new InvalidDataException("Totem string exceeds limit.");
            byte[] bytes = r.ReadBytes(n);
            if (bytes.Length != n) throw new EndOfStreamException();
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        internal static MasterIdolArtworkData Read(Stream stream)
        {
            if (stream == null) throw new InvalidDataException("Totem resource absent.");
            using (var r = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(8)) != "VMTOTEM1" || r.ReadUInt32() != 1)
                    throw new InvalidDataException("Totem format/version invalid.");
                uint count = r.ReadUInt32();
                if (count == 0 || count > 64) throw new InvalidDataException("Totem part count invalid.");
                var data = new MasterIdolArtworkData { Parts = new Part[(int)count] };
                uint total = 0;
                for (int i = 0; i < data.Parts.Length; i++)
                {
                    var part = new Part { Name = Text(r), Material = Text(r), Prefab = Text(r), Role = r.ReadByte() };
                    uint vertices = r.ReadUInt32();
                    if (part.Role > 2 || vertices == 0 || vertices > 120000 || vertices % 3 != 0 || total + vertices > 400000)
                        throw new InvalidDataException("Totem geometry limit invalid.");
                    total += vertices;
                    part.Vertices = new float[(int)vertices * 8];
                    for (int j = 0; j < part.Vertices.Length; j++)
                    {
                        float value = r.ReadSingle();
                        if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10000)
                            throw new InvalidDataException("Totem coordinate invalid.");
                        part.Vertices[j] = value;
                    }
                    data.Parts[i] = part;
                }
                if (r.BaseStream.ReadByte() != -1) throw new InvalidDataException("Totem trailing bytes.");
                return data;
            }
        }
    }
}
