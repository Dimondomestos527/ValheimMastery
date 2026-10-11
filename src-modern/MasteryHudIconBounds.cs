using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ValheimMastery
{
    // Generated from the installed 73-resource cohort, alpha > 8 plus 2px margin.
    // PNG pixels remain immutable. Changed/unknown bytes use the original full rect.
    // Hashing occurs only on first resource load, never during HUD updates.
    internal static class MasteryHudIconBounds
    {
        internal struct Bounds
        {
            internal readonly int X, Y, Width, Height;
            internal Bounds(int x, int y, int width, int height)
            { X=x; Y=y; Width=width; Height=height; }
        }
        private sealed class Entry
        {
            internal readonly int TextureWidth, TextureHeight;
            internal readonly string Hash;
            internal readonly Bounds Rect;
            internal Entry(string hash,int tw,int th,int x,int y,int width,int height)
            { Hash=hash; TextureWidth=tw; TextureHeight=th; Rect=new Bounds(x,y,width,height); }
        }
        private static readonly Dictionary<string,Entry> Pins = new Dictionary<string,Entry>(StringComparer.Ordinal)
        {
            { "ValheimMastery.Milestones.vm_icon_skill_axes_gold_v002.png", new Entry("83fff590901f2bca27ad47aac6c3eb6ccd398fd08be718803b0983cae301ee5c",499,512,67,0,432,512) },
            { "ValheimMastery.Milestones.vm_icon_skill_axes_silver_v003.png", new Entry("eccb915d4981acbd45e1da00a3e6f2d5221deb14bb8efc9ef9e7ce04a28293da",512,491,56,0,454,491) },
            { "ValheimMastery.Milestones.vm_icon_skill_axes_v003.png", new Entry("985285913c051c86bd838779f5a77e09ab9de301dae1adb9a3416027a1102e2c",473,512,74,8,398,470) },
            { "ValheimMastery.Milestones.vm_icon_skill_blocking_gold_v002.png", new Entry("e604e67971e11ab86211e0d46298f8038eca09e239d21e41fb347e5e5a76422a",472,512,103,7,251,494) },
            { "ValheimMastery.Milestones.vm_icon_skill_blocking_silver_v002.png", new Entry("f79ba4030a23e465b887426f317fc0692c734d0ab833e5b8898a261d9eb8067c",454,512,73,62,373,396) },
            { "ValheimMastery.Milestones.vm_icon_skill_blocking_v002.png", new Entry("8f56dc421d1a093209be30fff764170f0ec06c1bd97ccd3ce47da90ee9ab0024",479,512,58,81,358,359) },
            { "ValheimMastery.Milestones.vm_icon_skill_bloodmagic_deepnorth_v003.png", new Entry("1e259a9ac92999d1ac234150ea06cf61d770b6394a76f3809348b67a1643e7e7",480,512,17,26,463,480) },
            { "ValheimMastery.Milestones.vm_icon_skill_bloodmagic_gold_v003.png", new Entry("c78c4eac4b7105ff4a5ed06a869a875c66024bc0e13dc8997fe406bd0f768962",492,512,23,6,357,496) },
            { "ValheimMastery.Milestones.vm_icon_skill_bloodmagic_v006.png", new Entry("8b0400831e0e931b1bd7382489d4687f6804969c8e90b9b158aea2b3f50cebe9",512,498,41,3,459,495) },
            { "ValheimMastery.Milestones.vm_icon_skill_bows_silver_v002.png", new Entry("20874c0c4dcacb7b2e57d83ba409b42814c8adb3c2d725e0700c38972a6a59b1",470,512,148,7,294,487) },
            { "ValheimMastery.Milestones.vm_icon_skill_bows_v002.png", new Entry("1814d6e532d8a33ff58f3c68054bf53436049d2ed606c57db31d8bce7e93a42f",512,498,0,0,497,494) },
            { "ValheimMastery.Milestones.vm_icon_skill_bows_v006.png", new Entry("6a05370fd8e39e465cd2be8d24ea0378eef895550d44265503d9b0dd4fef6a5d",503,512,99,0,404,512) },
            { "ValheimMastery.Milestones.vm_icon_skill_clubs_gold_v005.png", new Entry("8f10dcc4c96a7def5452a5dfabb6532fbec24131d3ae1252e0b373ebaeb065c6",499,512,78,0,386,512) },
            { "ValheimMastery.Milestones.vm_icon_skill_clubs_silver_v002.png", new Entry("c94d8c9c55ca75e331f0190fd9dbdcdfcb6bf0a963317fae33e82a98395ce301",511,512,52,18,448,467) },
            { "ValheimMastery.Milestones.vm_icon_skill_clubs_v002.png", new Entry("a9577f0db959c2d37cbf6e2949664af0473ce3cfd02759f3eee1f07e59e43844",475,512,0,0,475,486) },
            { "ValheimMastery.Milestones.vm_icon_skill_cooking_gold_v002.png", new Entry("f76551be344eb4be5bc9f2bcefde0db28964e1e2d5a37d34a84dffef87557428",512,511,32,83,431,297) },
            { "ValheimMastery.Milestones.vm_icon_skill_cooking_silver_v004.png", new Entry("beba1d7dcf4f2130a55b76e756bdecd6f920b8d7e02f8c9f23860029b33de2e0",512,434,0,0,512,434) },
            { "ValheimMastery.Milestones.vm_icon_skill_cooking_v002.png", new Entry("0b46669cae0bbab16a86f8d203965c1748fe790d45215fa1f23809aff53ad7f1",512,508,59,83,418,327) },
            { "ValheimMastery.Milestones.vm_icon_skill_crafting_v001.png", new Entry("da17da8a4085ce8a3ae1f4fbe488a3daf3ba054d0e14965b38b726c276e4ae75",499,512,117,16,343,477) },
            { "ValheimMastery.Milestones.vm_icon_skill_crossbows_deepnorth_v002.png", new Entry("141655940ff348feb83df6dc3e281e151202bfc67a8217b7e5dc47ef8a25b8d6",481,512,82,15,387,483) },
            { "ValheimMastery.Milestones.vm_icon_skill_crossbows_gold_v003.png", new Entry("6189e2b86ea4d7c2338c22d0ca1e18ab2cae0788676cedf0bcb24d2edbb4a526",509,512,37,1,437,511) },
            { "ValheimMastery.Milestones.vm_icon_skill_crossbows_v002.png", new Entry("c5f505b490b1d76f64952c4489206a687adec5f5a5d992b66515c9e33f3b7ace",501,512,54,0,447,512) },
            { "ValheimMastery.Milestones.vm_icon_skill_dodge_v001.png", new Entry("783eeb7f9c7cf619376f85d44d15d9d654503cd19cfcb903648486a11ce6a4d4",512,510,73,79,381,336) },
            { "ValheimMastery.Milestones.vm_icon_skill_elementalmagic_deepnorth_v002.png", new Entry("5f5ba08826d7f510bd80aa600c0474465571a88a13ee6af88cf1d459e51a9104",512,507,34,27,439,464) },
            { "ValheimMastery.Milestones.vm_icon_skill_elementalmagic_silver_v002.png", new Entry("b9b2e3ad7ef53d58ee19a9894f5aa86f29989bf14f5508091f53df5607fdff32",372,512,119,15,128,489) },
            { "ValheimMastery.Milestones.vm_icon_skill_elementalmagic_v002.png", new Entry("c46b9c7430f2a26149435894aaa3261ebdc76d093cc8b0e0bbd6340ab7631bea",492,512,87,0,344,512) },
            { "ValheimMastery.Milestones.vm_icon_skill_farming_silver_v001.png", new Entry("025b440c82ef365895f849a07ea3b253089f943f2e4c2691d06abf519cbb0ddf",494,512,79,11,323,480) },
            { "ValheimMastery.Milestones.vm_icon_skill_farming_silver_v002.png", new Entry("4ad6c0d693819a45bf8549e41696dc833fe6045cb30aa77eb49a581219b0af40",498,512,88,50,346,398) },
            { "ValheimMastery.Milestones.vm_icon_skill_farming_v001.png", new Entry("043f639934f44741b7df8854d56e76620279603672a09c55f9823174400423f0",512,500,46,31,466,437) },
            { "ValheimMastery.Milestones.vm_icon_skill_fishing_v001.png", new Entry("65cbb22759ce3a8bf5fbdb27665e9c6f69134861a884a729edfe0a9088675864",512,507,32,17,470,467) },
            { "ValheimMastery.Milestones.vm_icon_skill_fists_gold_v003.png", new Entry("7b503d08cdbae175cb89778adfedaaf1f8fabec452ce5d2c51f618d6c9e01c57",488,512,1,90,486,317) },
            { "ValheimMastery.Milestones.vm_icon_skill_fists_silver_v001.png", new Entry("ea44dcd51bdda61b0477ad7382c8649422026724168f38b15231fbcf79e8eedf",477,512,42,58,420,394) },
            { "ValheimMastery.Milestones.vm_icon_skill_fists_v001.png", new Entry("7d61722c04f09194b44dbfed310ec31d2774006aa0aff29080b7c25c46a72b53",505,512,72,34,387,438) },
            { "ValheimMastery.Milestones.vm_icon_skill_jump_v001.png", new Entry("c108915322d023516197861cbbf83d2aedf8244df9e61c3d865553cc15ee75bc",494,512,60,37,376,437) },
            { "ValheimMastery.Milestones.vm_icon_skill_knives_gold_v001.png", new Entry("369d52c79b2a4e89588ddaf93dd8507408d82b9283b92946d40a5bf793450cf7",494,512,51,23,443,489) },
            { "ValheimMastery.Milestones.vm_icon_skill_knives_silver_v003.png", new Entry("e97dbc1d7738cf3e9cb06c1a5a31a883f564b3b8d3cc129868a87217806a7c03",403,512,113,7,290,478) },
            { "ValheimMastery.Milestones.vm_icon_skill_knives_v001.png", new Entry("9092b2cd3cbec168239819baacfc5dc7c691cb0a46d3722243fe3fbbd690d26d",492,512,40,17,424,467) },
            { "ValheimMastery.Milestones.vm_icon_skill_pickaxes_silver_v001.png", new Entry("c31c706c017f1432e79c3f46d874702c41c1420d4cc8edfb416de4a5af916078",512,505,38,7,453,468) },
            { "ValheimMastery.Milestones.vm_icon_skill_pickaxes_v001.png", new Entry("121cc684ad8258f1dc4cec4bb17795f3518d7874f8ee8112e9e2c83771c35be0",474,512,0,35,465,439) },
            { "ValheimMastery.Milestones.vm_icon_skill_pickaxes_v002.png", new Entry("1ecd3bc608d136bb1b36b17e0ff84ebe4b7bcb1eeeffa8abcec85dede7de96ae",512,499,19,24,476,450) },
            { "ValheimMastery.Milestones.vm_icon_skill_polearms_silver_v002.png", new Entry("ac0aec8ea39ca02efb723c1b90f63afbd062293bb0168c831ea26587776790b1",480,512,92,0,329,508) },
            { "ValheimMastery.Milestones.vm_icon_skill_polearms_v001.png", new Entry("2b3e6f0b7735027c158f4c2a979ec172937c8126620b57562f300e15d1b96a7f",512,511,0,0,512,511) },
            { "ValheimMastery.Milestones.vm_icon_skill_polearms_v002.png", new Entry("6d6c8cce6718f385ec066a4cba186c04a9214c2c4a164bf4afdd310ebb9eb112",504,512,3,5,495,505) },
            { "ValheimMastery.Milestones.vm_icon_skill_ride_v001.png", new Entry("98bbda77bde382befa96af19ecef87f8800c6e96376c55a577cd3db7f66acf7f",489,512,44,42,435,422) },
            { "ValheimMastery.Milestones.vm_icon_skill_run_v001.png", new Entry("df5609eab6c4394708f3ff6f3f664731b99a3cb71da761604d9c42ab5c6d02f5",503,512,24,78,435,337) },
            { "ValheimMastery.Milestones.vm_icon_skill_sneak_v001.png", new Entry("305c738b7c6df4407463a224ac93a908d47ae8aa285cf1a36bff8d269902e68c",476,512,72,53,357,412) },
            { "ValheimMastery.Milestones.vm_icon_skill_spears_silver_v001.png", new Entry("d1dffd5230d9888da5a5c24df53f15dc740086654dedfad76768f0626d91dd5a",505,512,23,4,470,482) },
            { "ValheimMastery.Milestones.vm_icon_skill_spears_silver_v003.png", new Entry("f69493cfce5d12bb2dcaf963df5ecfad52d2a63d10ce5b05031ef154140b2b40",486,512,0,1,486,509) },
            { "ValheimMastery.Milestones.vm_icon_skill_spears_v001.png", new Entry("d8cdb911c600e63bb75ecc7488c5f88551fc2bf6ac470532a62307e97fe02949",492,512,0,0,492,494) },
            { "ValheimMastery.Milestones.vm_icon_skill_swim_v001.png", new Entry("a49b43d61108b026a282babe3c89370909cb08714f6ba9613bc99421ca5476d1",512,503,23,80,468,322) },
            { "ValheimMastery.Milestones.vm_icon_skill_swords_silver_v001.png", new Entry("6a1d24054bfa148dcc071132f8f1099b578ff5c4a369ab624f792ddf0eed1245",512,496,64,12,435,461) },
            { "ValheimMastery.Milestones.vm_icon_skill_swords_silver_v002.png", new Entry("dbb3d5b26b47b9eda12bd1da88404837cb1df688fd4da7fe318088d6c37b1730",512,511,0,0,512,511) },
            { "ValheimMastery.Milestones.vm_icon_skill_swords_v002.png", new Entry("7a3e1b56515dad69f9afc78bc63bc8cad2eadfb5cf17623acf3025d93f3fb01a",512,509,36,9,462,494) },
            { "ValheimMastery.Milestones.vm_icon_skill_woodcutting_silver_v001.png", new Entry("9a556c5a41fcd3133402e46987b9358d494cab5291f2e4ac1eb7c67a5727086e",512,510,31,15,446,471) },
            { "ValheimMastery.Milestones.vm_icon_skill_woodcutting_v001.png", new Entry("62231beed48be069c1cc8b7db75ee4bd0f599a2fc53c0dc9b64c7317d6b56d68",476,512,31,34,445,432) },
            { "ValheimMastery.Milestones.vm_icon_skill_woodcutting_v002.png", new Entry("81b7994e949827414110f7f55ea3e8e8b5f1c5528d373c200dc5325f0ffa3bba",510,512,39,37,435,441) },
        };
        internal static bool TryGet(string resource,byte[] bytes,int width,int height,out Bounds bounds)
        {
            bounds=default(Bounds);
            Entry entry;
            if(resource==null || bytes==null || bytes.Length==0 || bytes.Length>4*1024*1024 ||
               !Pins.TryGetValue(resource,out entry) || width!=entry.TextureWidth || height!=entry.TextureHeight)
                return false;
            var rect=entry.Rect;
            if(rect.Width<=0 || rect.Height<=0 || rect.X<0 || rect.Y<0 ||
               rect.X>width-rect.Width || rect.Y>height-rect.Height) return false;
            string hash;
            using(var sha=SHA256.Create())
                hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            if(!string.Equals(hash,entry.Hash,StringComparison.Ordinal)) return false;
            bounds=rect;return true;
        }
    }
}
