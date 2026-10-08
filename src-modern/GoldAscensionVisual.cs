using UnityEngine;

namespace ValheimMastery
{
    // Presentation only. Unlock persistence and the nearby relay stay in
    // GoldCraftingService; all forge feedback follows the generic gold frame.
    internal static class GoldAscensionVisual
    {
        internal static void Play(Player player, bool localRecognition)
        {
            if (player == null || Application.isBatchMode || !MasteryPlugin.Settings.Enabled.Value) return;
            GameObject root = new GameObject("ValheimMastery_GoldAscension");
            root.AddComponent<GoldAscensionSequence>().Initialize(player, localRecognition);
        }
    }

    internal sealed class GoldAscensionSequence : MonoBehaviour
    {
        private Player _player;
        private float _received, _started;
        private float _nextEmber = 1.15f;
        private int _emberBursts;
        private bool _localRecognition, _begun, _ignitionPlayed, _impactPlayed, _categoryMessagePlayed;

        internal void Initialize(Player player, bool localRecognition)
        { _player = player; _localRecognition = localRecognition; _received = Time.unscaledTime; }

        private void Update()
        {
            if (_player == null || !MasteryPlugin.Settings.Enabled.Value) { Destroy(gameObject); return; }
            if (!_begun)
            {
                // Observers share the caster's five-second frame delay. Locally,
                // also wait for actual destruction, including a replaced frame.
                if (Time.unscaledTime - _received < MasteryMilestonePresentation.Duration + .10f ||
                    (_localRecognition && _player == Player.m_localPlayer && MasteryMilestonePresentation.IsActive)) return;
                _begun = true; _started = Time.unscaledTime;
            }
            float age = Time.unscaledTime - _started;
            Vector3 center = _player.GetCenterPoint();
            if (!_ignitionPlayed && age >= .05f)
            {
                _ignitionPlayed = true;
                // Anchor to the delivered recognition, even after a frame stall.
                _started = Time.unscaledTime - .05f; age = .05f;
                if (_localRecognition && _player == Player.m_localPlayer)
                    _player.Message(MessageHud.MessageType.Center,
                        GoldUiLocalization.Text("VÖLUNDR RECOGNIZES YOUR MASTERY", "ВЬОЛУНДР ВИЗНАЄ ТВОЮ МАЙСТЕРНІСТЬ"), 0, null);
                Vector3 right = _player.transform.right;
                Vector3 hands = center + Vector3.up * .08f;
                GoldForgeCosmetics.Burst("vfx_ForgeAddFuel", hands - right * .38f, .95f, 1.35f, 64, transform);
                GoldForgeCosmetics.Burst("vfx_ForgeAddFuel", hands + right * .38f, .95f, 1.35f, 64, transform);
            }
            if (!_impactPlayed && age >= .45f)
            {
                _impactPlayed = true;
                Vector3 ground = GroundPoint(_player.transform.position);
                // One decisive forge impact, not a succession of explosions.
                GoldForgeCosmetics.Burst("vfx_Place_forge", ground, 1.60f, 1.45f, 160, transform);
                GoldForgeImpactRing.Play(ground, 2.70f, transform);
                GoldForgeRisingEmbers.Play(_player, transform);
                PerkAudioService.Play("crafting100_ascension_impact", "sfx_gui_craftitem_forge",
                    ground, .8f, .95f, .84f);
            }
            if (!_categoryMessagePlayed && age >= 3.25f)
            {
                _categoryMessagePlayed = true;
                // Recognition remains the sole center message for at least 3.2 s.
                if (_localRecognition && _player == Player.m_localPlayer)
                    _player.Message(MessageHud.MessageType.Center,
                        GoldUiLocalization.Text("MASTER IDOLS AWAIT THE NEXT PHASE",
                            "МАЙСТЕРНІ ІДОЛИ — У НАСТУПНІЙ ФАЗІ РОЗВИТКУ"), 0, null);
            }
            if (age >= _nextEmber && _emberBursts < 4)
            {
                float phase = _emberBursts / 3f;
                Vector3 offset = _player.transform.right * (Mathf.Sin(_emberBursts * 2.4f) * .46f);
                Vector3 point = _player.transform.position + Vector3.up * (1.0f + phase * 1.25f) + offset;
                GoldForgeCosmetics.Burst("vfx_HitSparks", point, .72f, .85f, 48, transform);
                _emberBursts++; _nextEmber = age + .55f;
            }
            if (age >= 4.40f) Destroy(gameObject);
        }

        private static Vector3 GroundPoint(Vector3 position)
        {
            int mask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default");
            return Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, mask)
                ? hit.point + Vector3.up * .04f : position + Vector3.up * .04f;
        }
    }

    // A global 24-instance ceiling also bounds simultaneous nearby ascensions.
    // Clones stay below the established cosmetic safety boundary. Retuning a
    // separate owned instance never changes a pooled or shared native template.
    internal static class GoldForgeCosmetics
    {
        internal static int Active;
        private const int ActiveLimit = 24;

        internal static GameObject Create(string prefabName, Vector3 position, float scale,
            float lifetime, int budget, Transform owner)
        {
            if (Player.m_localPlayer == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value || Active >= ActiveLimit) return null;
            GameObject root = PerkNativeFeedback.CreateVisualOnly(NativePerkAssetResolver.Resolve(prefabName));
            if (root == null) return null;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            budget = Mathf.Clamp(budget, 1, 160);
            if (systems.Length == 0 || systems.Length > budget) { UnityEngine.Object.Destroy(root); return null; }
            int perSystem = Mathf.Max(1, budget / systems.Length);
            foreach (ParticleSystem system in systems)
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.loop = false; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
                main.maxParticles = Mathf.Min(main.maxParticles, perSystem);
                main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, lifetime);
                var subEmitters = system.subEmitters; subEmitters.enabled = false;
            }
            // The forge effect's particles carry the heat; no additional light flood.
            foreach (Light light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
            root.transform.position = position;
            root.transform.localScale *= Mathf.Clamp(scale, .1f, 2f);
            root.transform.SetParent(owner, true);
            root.AddComponent<GoldForgeCosmeticLifetime>().Arm(lifetime);
            return root;
        }

        internal static void Burst(string prefabName, Vector3 position, float scale,
            float lifetime, int budget, Transform owner)
        {
            GameObject root = Create(prefabName, position, scale, lifetime, budget, owner);
            if (root == null) return;
            root.SetActive(true);
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (root == null || !root.activeInHierarchy) return;
                foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true)) system.Play(false);
            });
        }

        internal static ParticleSystem ControlledParticles(GameObject root, int capacity)
        {
            ParticleSystem selected = null;
            int best = int.MinValue;
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var emission = system.emission; emission.enabled = false;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.renderMode == ParticleSystemRenderMode.Mesh || renderer.sharedMaterial == null) continue;
                string name = (system.name + " " + renderer.sharedMaterial.name).ToLowerInvariant();
                int score = name.Contains("ember") ? 4 : name.Contains("spark") ? 3 : 0;
                if (name.Contains("smoke") || name.Contains("dust") || name.Contains("flare") || name.Contains("glow")) score -= 10;
                if (score <= best) continue;
                best = score; selected = system;
            }
            if (selected == null || best < 0) return null;
            var main = selected.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = Mathf.Clamp(capacity, 1, 160);
            main.loop = true;
            main.startSpeed = 0f; main.gravityModifier = 0f;
            ParticleSystemRenderer selectedRenderer = selected.GetComponent<ParticleSystemRenderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer != selectedRenderer) renderer.enabled = false;
            // Keep the real renderer/material, flipbook and particle vertex streams.
            return selected;
        }

        internal static void Cancel(GameObject root)
        {
            if (root == null) return;
            // An inactive first-use wrapper need never receive OnDestroy.
            root.GetComponent<GoldForgeCosmeticLifetime>()?.Release();
            DeferredNativeVfx deferred = root.GetComponent<DeferredNativeVfx>();
            if (deferred != null) deferred.Abort(); else UnityEngine.Object.Destroy(root);
        }
    }

    internal sealed class GoldForgeCosmeticLifetime : MonoBehaviour
    {
        private float _until;
        private bool _counted, _ready;
        internal void Arm(float lifetime)
        {
            _counted = true; GoldForgeCosmetics.Active++;
            float duration = Mathf.Clamp(lifetime, .1f, 4f);
            NativeVfxSafeFrame.AfterReady(gameObject, () =>
            {
                if (this == null || !gameObject.activeInHierarchy) return;
                _until = Time.unscaledTime + duration; _ready = true;
            });
        }
        private void Update()
        {
            if ((_ready && Time.unscaledTime >= _until) || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) Destroy(gameObject);
        }
        internal void Release()
        {
            if (_counted) { _counted = false; GoldForgeCosmetics.Active = Mathf.Max(0, GoldForgeCosmetics.Active - 1); }
        }
        private void OnDestroy() => Release();
    }

    // A wider, broken band of native sparks conveys heat pressure without a
    // solid neon circle. Terrain samples are fixed once, not traced each frame.
    internal sealed class GoldForgeImpactRing : MonoBehaviour
    {
        private const int Segments = 96;
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[Segments];
        private readonly Vector3[] _ground = new Vector3[Segments];
        private ParticleSystem _effect;
        private Vector3 _center;
        private float _started;

        internal static void Play(Vector3 center, float radius, Transform owner = null)
        {
            GameObject root = GoldForgeCosmetics.Create("vfx_HitSparks", center, 1f, 1.25f, Segments, owner);
            if (root == null) return;
            GoldForgeImpactRing ring = root.AddComponent<GoldForgeImpactRing>();
            if (!ring.Initialize(center, radius)) { GoldForgeCosmetics.Cancel(root); return; }
            root.SetActive(true);
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (ring == null || root == null || !root.activeInHierarchy) return;
                ring._started = Time.unscaledTime; ring._effect.Play(false);
            });
        }

        private bool Initialize(Vector3 center, float radius)
        {
            _center = center; radius = Mathf.Clamp(radius, .4f, 3f); _started = Time.unscaledTime;
            _effect = GoldForgeCosmetics.ControlledParticles(gameObject, Segments);
            if (_effect == null) return false;
            int mask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default");
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                float band = radius * (.94f + (i % 5) * .025f);
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * band, 0f, Mathf.Sin(angle) * band);
                _ground[i] = Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, mask)
                    ? hit.point + Vector3.up * .06f : point;
            }
            return true;
        }

        private void LateUpdate()
        {
            if (_effect == null || GetComponent<DeferredNativeVfx>()?.IsPrepared == false) return;
            float age = Time.unscaledTime - _started;
            float fade = Mathf.Clamp01(1f - age / 1.10f);
            float expand = 1f - Mathf.Pow(1f - Mathf.Clamp01(age / .32f), 3f);
            if (fade <= 0f) { _effect.SetParticles(_particles, 0); return; }
            for (int i = 0; i < Segments; i++)
            {
                Vector3 point = _ground[i];
                _particles[i].position = new Vector3(Mathf.Lerp(_center.x, point.x, expand), point.y + age * .08f,
                    Mathf.Lerp(_center.z, point.z, expand));
                _particles[i].velocity = Vector3.zero;
                _particles[i].startSize = .11f + (i % 4) * .013f;
                _particles[i].startColor = new Color(1f, .46f + (i % 3) * .035f, .12f, fade * (.58f + (i % 5) * .07f));
                _particles[i].randomSeed = (uint)(i + 1);
                _particles[i].startLifetime = 1.10f;
                _particles[i].remainingLifetime = Mathf.Max(.01f, 1.10f - age);
            }
            _effect.SetParticles(_particles, Segments);
        }
    }

    // A finite ember column follows the smith and rises to shoulder/head height.
    // The intact native spark renderer avoids borrowed opaque billboard materials.
    internal sealed class GoldForgeRisingEmbers : MonoBehaviour
    {
        private const int Capacity = 128;
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[Capacity];
        private ParticleSystem _effect;
        private Player _player;
        private float _started;

        internal static void Play(Player player, Transform owner)
        {
            if (player == null) return;
            GameObject root = GoldForgeCosmetics.Create("vfx_HitSparks", player.transform.position, 1f, 3.25f, Capacity, owner);
            if (root == null) return;
            GoldForgeRisingEmbers embers = root.AddComponent<GoldForgeRisingEmbers>();
            embers._effect = GoldForgeCosmetics.ControlledParticles(root, Capacity);
            if (embers._effect == null) { GoldForgeCosmetics.Cancel(root); return; }
            embers._player = player; embers._started = Time.unscaledTime;
            root.SetActive(true);
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (embers == null || root == null || !root.activeInHierarchy) return;
                embers._started = Time.unscaledTime; embers._effect.Play(false);
            });
        }

        private void LateUpdate()
        {
            if (_effect == null || _player == null) { Destroy(gameObject); return; }
            if (GetComponent<DeferredNativeVfx>()?.IsPrepared == false) return;
            float age = Time.unscaledTime - _started;
            float fade = Mathf.SmoothStep(0f, 1f, age / .18f) *
                (1f - Mathf.SmoothStep(0f, 1f, (age - 2.4f) / .65f));
            if (fade <= 0f && age > .18f) { _effect.SetParticles(_particles, 0); return; }
            Vector3 center = _player.transform.position;
            for (int i = 0; i < Capacity; i++)
            {
                float rise = Mathf.Repeat(age * (.42f + (i % 7) * .018f) + i * .618034f, 1f);
                float angle = i * 2.39996f + age * .65f;
                float radius = .28f + .44f * Mathf.Sqrt((i + .5f) / Capacity);
                _particles[i].position = center + new Vector3(Mathf.Cos(angle) * radius,
                    .32f + rise * 2.45f, Mathf.Sin(angle) * radius);
                _particles[i].velocity = new Vector3(-Mathf.Sin(angle) * .16f, 1.35f, Mathf.Cos(angle) * .16f);
                _particles[i].startSize = .05f + (i % 5) * .008f;
                _particles[i].startColor = new Color(1f, .43f + (i % 4) * .045f, .12f,
                    fade * .80f * Mathf.Sin(rise * Mathf.PI));
                _particles[i].randomSeed = (uint)(i + 1);
                _particles[i].startLifetime = 2.4f;
                _particles[i].remainingLifetime = Mathf.Max(.01f, 2.4f * (1f - rise));
            }
            _effect.SetParticles(_particles, Capacity);
        }
    }
}
