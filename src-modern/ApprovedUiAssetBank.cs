using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

namespace ValheimMastery
{
    internal static class ApprovedUiAssetBank
    {
        private sealed class AssetSpec
        {
            internal readonly string Resource, Hash; internal readonly int Width, Height; internal readonly Rect Rect;
            internal AssetSpec(string resource, string hash, int width, int height, int x, int y, int w, int h)
            { Resource = resource; Hash = hash; Width = width; Height = height; Rect = new Rect(x, y, w, h); }
        }
        private static readonly Dictionary<string, Sprite> Assets = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        // Exact 15 approved exports from runtime manifest B596650E; no large master import.
        private static readonly Dictionary<string, AssetSpec> Specs = new Dictionary<string, AssetSpec>(StringComparer.Ordinal)
        {
            { "Enchantments/meadows", new AssetSpec("ValheimMastery.Assets.Enchantments.meadows.png", "42be6930c709f7f0a82daa78ed4e6a0492375765047b868bee2a482a6a810b24", 512, 512, 62, 33, 388, 456) },
            { "Enchantments/blackforest", new AssetSpec("ValheimMastery.Assets.Enchantments.blackforest.png", "92bd23e09ff94c616043d868292d5572037b21a7c456b59211c98c8da4e7d73a", 512, 512, 95, 27, 322, 464) },
            { "Enchantments/swamp", new AssetSpec("ValheimMastery.Assets.Enchantments.swamp.png", "d5a81750e1dfa3c4ef676ff705891bbf7e1337a43e4bc5e1cf19d2a8a5143832", 512, 512, 96, 35, 320, 440) },
            { "Enchantments/mountains", new AssetSpec("ValheimMastery.Assets.Enchantments.mountains.png", "3e55a30112aab38e575bd3641e21f1e1e6adf35bfbe638ac8d59f0535bbce130", 512, 512, 43, 28, 426, 463) },
            { "Enchantments/plains", new AssetSpec("ValheimMastery.Assets.Enchantments.plains.png", "4b001102fcb1cd96b0ab39fb429788319e602a6bd574556005cb14b28e8d6088", 512, 512, 89, 53, 348, 396) },
            { "Enchantments/mistlands", new AssetSpec("ValheimMastery.Assets.Enchantments.mistlands.png", "6d4ca08484c34444d2310b8ce9dd1a0dec2f11c53eea6b2befa2864f15ef7dce", 512, 512, 85, 21, 342, 471) },
            { "Run70/burial_chambers", new AssetSpec("ValheimMastery.Assets.Run70.burial_chambers.png", "fcf30e984888066241e7c7a11f758da28bb45fd21c7b9033b396c93600890ded", 128, 128, 9, 29, 110, 61) },
            { "Run70/troll_cave", new AssetSpec("ValheimMastery.Assets.Run70.troll_cave.png", "7eda5911ef85014bab8bad9bfbda43731ad4c08db0dba5f35d349189d921d0ff", 128, 128, 12, 24, 105, 77) },
            { "Run70/sunken_crypt", new AssetSpec("ValheimMastery.Assets.Run70.sunken_crypt.png", "af868aee53087b375227f8473e40a5f39ff162e51e8fbd62c073c885aa6e7029", 128, 128, 19, 34, 90, 60) },
            { "Run70/frost_cave", new AssetSpec("ValheimMastery.Assets.Run70.frost_cave.png", "41a56e9a9d6e324dc017f8fd4739e6e53d98822e7edf8af9418f5eec8c123f6c", 128, 128, 13, 21, 101, 85) },
            { "Run70/infested_mine", new AssetSpec("ValheimMastery.Assets.Run70.infested_mine.png", "b0dc73b436508ebf17a60f0c1b11be03b31803e1da4d7930efa8042cabf1ab9c", 128, 128, 19, 11, 90, 105) },
            { "Run70/ashlands_fortress", new AssetSpec("ValheimMastery.Assets.Run70.ashlands_fortress.png", "28aaada6f8f681ab33994951f52436744dbf12c853b79a3a7f6517616b752c90", 128, 128, 15, 36, 97, 54) },
            { "Run70/the_hole", new AssetSpec("ValheimMastery.Assets.Run70.the_hole.png", "a29182c00eef610dbc9cd28eb900391e000b6a9210dca775160f36f95e726fe8", 128, 128, 22, 19, 84, 89) },
            { "Run70/morkborg", new AssetSpec("ValheimMastery.Assets.Run70.morkborg.png", "ec3a0745b5b6060c92aeeac8ff5de39096976e24d83b53f75d98f911fe2cf0c4", 128, 128, 33, 16, 62, 95) },
            { "Run70/bear_cave", new AssetSpec("ValheimMastery.Assets.Run70.bear_cave.png", "612ad6962ffdc6076999ad4682aa32c2b51f32b39d485de8074ba4290b3d17d8", 128, 128, 5, 32, 118, 55) },
        };
        internal static Sprite Enchantment(string choice)
        {
            string id;
            switch (choice)
            {
                case "Meadows": id = "meadows"; break; case "BlackForest": id = "blackforest"; break;
                case "Swamp": id = "swamp"; break; case "Mountain": id = "mountains"; break;
                case "Plains": id = "plains"; break; case "Mistlands": id = "mistlands"; break;
                default: return null;
            }
            return LoadApproved("Enchantments/" + id);
        }
        internal static Sprite Run70(string category) => Run70MapMarkerState.Known(category) ? LoadApproved("Run70/" + category) : null;
        private static Sprite LoadApproved(string key)
        {
            if (Application.isBatchMode || !Specs.TryGetValue(key, out var spec)) return null;
            if (Assets.TryGetValue(key, out var cached)) return cached;
            Assets[key] = null; Texture2D texture = null;
            try
            {
                using (var stream = typeof(ApprovedUiAssetBank).Assembly.GetManifestResourceStream(spec.Resource))
                {
                    if (stream == null || stream.Length < 8 || stream.Length > 262144) return null;
                    byte[] data = new byte[(int)stream.Length]; int offset = 0;
                    while (offset < data.Length) { int read = stream.Read(data, offset, data.Length - offset); if (read <= 0) return null; offset += read; }
                    using (var sha = SHA256.Create())
                        if (!string.Equals(BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""), spec.Hash, StringComparison.OrdinalIgnoreCase)) return null;
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!texture.LoadImage(data, true) || texture.width != spec.Width || texture.height != spec.Height)
                    { UnityEngine.Object.Destroy(texture); return null; }
                    texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
                    var sprite = Sprite.Create(texture, spec.Rect, new Vector2(.5f, .5f), 100f);
                    if (sprite == null) { UnityEngine.Object.Destroy(texture); return null; }
                    Assets[key] = sprite; return sprite;
                }
            }
            catch { if (texture != null) UnityEngine.Object.Destroy(texture); return null; }
        }
        internal static void Shutdown()
        {
            foreach (var sprite in Assets.Values)
                if (sprite != null) { var texture = sprite.texture; UnityEngine.Object.Destroy(sprite); if (texture != null) UnityEngine.Object.Destroy(texture); }
            Assets.Clear();
        }
    }
}
