using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ValheimMastery
{
    // Scoped human-approved milestone art; does not replace shared skill/proc/buff icons.
    // Each message owns at most one frame and one icon; no persistent texture cache.
    internal sealed class MasteryMilestoneArtwork : MonoBehaviour
    {
        private const string ResourcePrefix = "ValheimMastery.Milestones.";
        private readonly List<Sprite> _sprites = new List<Sprite>(2);
        private readonly List<Texture2D> _textures = new List<Texture2D>(2);
        private static bool _warned;
        private static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "swords_35", "vm_icon_skill_swords_v002.png" },
            { "swords_70", "vm_icon_skill_swords_silver_v002.png" },
            { "swords_100", "vm_icon_skill_swords_silver_v001.png" },
            { "axes_35", "vm_icon_skill_axes_v003.png" },
            { "axes_70", "vm_icon_skill_axes_silver_v003.png" },
            { "axes_100", "vm_icon_skill_axes_gold_v002.png" },
            { "clubs_35", "vm_icon_skill_clubs_v002.png" },
            { "clubs_70", "vm_icon_skill_clubs_silver_v002.png" },
            { "clubs_100", "vm_icon_skill_clubs_gold_v005.png" },
            { "knives_35", "vm_icon_skill_knives_v001.png" },
            { "knives_70", "vm_icon_skill_knives_silver_v003.png" },
            { "knives_100", "vm_icon_skill_knives_gold_v001.png" },
            { "spears_35", "vm_icon_skill_spears_v001.png" },
            { "spears_70", "vm_icon_skill_spears_silver_v003.png" },
            { "spears_100", "vm_icon_skill_spears_silver_v001.png" },
            { "polearms_35", "vm_icon_skill_polearms_v002.png" },
            { "polearms_70", "vm_icon_skill_polearms_v001.png" },
            { "polearms_100", "vm_icon_skill_polearms_silver_v002.png" },
            { "bows_35", "vm_icon_skill_bows_v002.png" },
            { "bows_70", "vm_icon_skill_bows_v006.png" },
            { "bows_100", "vm_icon_skill_bows_silver_v002.png" },
            { "crossbows_35", "vm_icon_skill_crossbows_v002.png" },
            { "crossbows_70", "vm_icon_skill_crossbows_gold_v003.png" },
            { "crossbows_100", "vm_icon_skill_crossbows_deepnorth_v002.png" },
            { "fists_35", "vm_icon_skill_fists_v001.png" },
            { "fists_70", "vm_icon_skill_fists_silver_v001.png" },
            { "fists_100", "vm_icon_skill_fists_gold_v003.png" },
            { "blocking_35", "vm_icon_skill_blocking_v002.png" },
            { "blocking_70", "vm_icon_skill_blocking_silver_v002.png" },
            { "blocking_100", "vm_icon_skill_blocking_gold_v002.png" },
            { "dodge_35", "vm_icon_skill_dodge_v001.png" },
            { "dodge_70", "vm_icon_skill_dodge_v001.png" },
            { "dodge_100", "vm_icon_skill_dodge_v001.png" },
            { "sneak_35", "vm_icon_skill_sneak_v001.png" },
            { "sneak_70", "vm_icon_skill_sneak_v001.png" },
            { "sneak_100", "vm_icon_skill_sneak_v001.png" },
            { "run_35", "vm_icon_skill_run_v001.png" },
            { "run_70", "vm_icon_skill_run_v001.png" },
            { "run_100", "vm_icon_skill_run_v001.png" },
            { "jump_35", "vm_icon_skill_jump_v001.png" },
            { "jump_70", "vm_icon_skill_jump_v001.png" },
            { "jump_100", "vm_icon_skill_jump_v001.png" },
            { "swim_35", "vm_icon_skill_swim_v001.png" },
            { "swim_70", "vm_icon_skill_swim_v001.png" },
            { "swim_100", "vm_icon_skill_swim_v001.png" },
            { "ride_35", "vm_icon_skill_ride_v001.png" },
            { "ride_70", "vm_icon_skill_ride_v001.png" },
            { "ride_100", "vm_icon_skill_ride_v001.png" },
            { "elementalmagic_35", "vm_icon_skill_elementalmagic_v002.png" },
            { "elementalmagic_70", "vm_icon_skill_elementalmagic_silver_v002.png" },
            { "elementalmagic_100", "vm_icon_skill_elementalmagic_deepnorth_v002.png" },
            { "bloodmagic_35", "vm_icon_skill_bloodmagic_v006.png" },
            { "bloodmagic_70", "vm_icon_skill_bloodmagic_gold_v003.png" },
            { "bloodmagic_100", "vm_icon_skill_bloodmagic_deepnorth_v003.png" },
            { "woodcutting_35", "vm_icon_skill_woodcutting_v002.png" },
            { "woodcutting_70", "vm_icon_skill_woodcutting_v001.png" },
            { "woodcutting_100", "vm_icon_skill_woodcutting_silver_v001.png" },
            { "pickaxes_35", "vm_icon_skill_pickaxes_v002.png" },
            { "pickaxes_70", "vm_icon_skill_pickaxes_v001.png" },
            { "pickaxes_100", "vm_icon_skill_pickaxes_silver_v001.png" },
            { "fishing_35", "vm_icon_skill_fishing_v001.png" },
            { "fishing_70", "vm_icon_skill_fishing_v001.png" },
            { "fishing_100", "vm_icon_skill_fishing_v001.png" },
            { "farming_35", "vm_icon_skill_farming_v001.png" },
            { "farming_70", "vm_icon_skill_farming_silver_v002.png" },
            { "farming_100", "vm_icon_skill_farming_silver_v001.png" },
            { "cooking_35", "vm_icon_skill_cooking_v002.png" },
            { "cooking_70", "vm_icon_skill_cooking_silver_v003.png" },
            { "cooking_100", "vm_icon_skill_cooking_gold_v002.png" },
            { "crafting_35", "vm_icon_skill_crafting_v001.png" },
            { "crafting_70", "vm_icon_skill_crafting_v001.png" },
            { "crafting_100", "vm_icon_skill_crafting_v001.png" },
        };

        internal bool TryLoad(string perkId, int milestone, out Sprite frame, out Sprite icon)
        {
            frame = null;
            icon = null;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return false;
            if (milestone != 35 && milestone != 70 && milestone != 100) return false;
            if (string.IsNullOrEmpty(perkId) || !Icons.TryGetValue(perkId, out string filename)) return false;
            try
            {
                string tier = milestone == 100 ? "gold" : milestone == 70 ? "silver" : "bronze";
                frame = Load("frame_" + tier + ".png");
                icon = Load(filename);
                return true;
            }
            catch (Exception ex)
            {
                frame = null; icon = null;
                Release();
                if (!_warned)
                {
                    _warned = true;
                    MasteryPlugin.Log?.LogWarning("Milestone artwork unavailable; using native fallback. " + ex.Message);
                }
                return false;
            }
        }

        private Sprite Load(string filename)
        {
            byte[] bytes;
            using (Stream stream = typeof(MasteryMilestoneArtwork).Assembly.GetManifestResourceStream(ResourcePrefix + filename))
            {
                if (stream == null || stream.Length <= 0 || stream.Length > 4 * 1024 * 1024)
                    throw new InvalidDataException("Missing or oversized milestone resource: " + filename);
                bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) throw new EndOfStreamException(filename);
                    read += count;
                }
            }
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _textures.Add(texture);
            texture.name = "MasteryMilestone_" + filename;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            if (!ImageConversion.LoadImage(texture, bytes, true))
                throw new InvalidDataException("Could not decode milestone PNG: " + filename);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            _sprites.Add(sprite);
            return sprite;
        }

        private void Release()
        {
            foreach (Sprite sprite in _sprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
            foreach (Texture2D texture in _textures) if (texture != null) UnityEngine.Object.Destroy(texture);
            _sprites.Clear(); _textures.Clear();
        }

        private void OnDestroy() { Release(); }
    }
}
