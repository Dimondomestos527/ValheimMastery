using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal static class MasteryMilestonePresentation
    {
        private static GameObject _active;
        internal const float Duration = 5f;
        internal static bool IsActive => _active != null;

        internal static void Show(Player player, Skills.SkillType skill, int milestone, PerkDefinition perk, bool achievementSound = true)
        {
            if (player == null || player != Player.m_localPlayer || perk == null) return;
            if (_active != null) Object.Destroy(_active);

            Color accent = milestone >= 100 ? new Color(1f, .78f, .18f) :
                (milestone >= 70 ? new Color(.80f, .88f, 1f) : new Color(.80f, .43f, .18f));
            _active = new GameObject("ValheimMastery_PerkUnlock");
            Canvas canvas = _active.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2500;
            CanvasScaler scaler = _active.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920f, 1080f);
            CanvasGroup group = _active.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            MasteryMilestoneArtwork artwork = _active.AddComponent<MasteryMilestoneArtwork>();
            Sprite frameSprite = null, itemSprite = null;
            bool customArtwork = MasteryPlugin.Settings.EnableMilestoneMessages.Value &&
                artwork.TryLoad(perk.Id, milestone, out frameSprite, out itemSprite);

            GameObject panel = Ui("Panel", _active.transform);
            RectTransform pr = panel.AddComponent<RectTransform>();
            pr.anchorMin = pr.anchorMax = new Vector2(.5f, 1f);
            pr.pivot = new Vector2(.5f, 1f);
            pr.anchoredPosition = new Vector2(0f, -10f);
            pr.sizeDelta = customArtwork ? new Vector2(640f, 360f) : new Vector2(560f, 300f);
            Image background = panel.AddComponent<Image>();
            background.sprite = frameSprite;
            background.preserveAspect = customArtwork;
            background.color = customArtwork ? Color.white : new Color(.035f, .025f, .018f, .90f);
            if (!customArtwork)
            {
                Outline outline = panel.AddComponent<Outline>(); outline.effectColor = accent; outline.effectDistance = new Vector2(4f, -4f);
            }

            Sprite icon = customArtwork ? itemSprite : player.GetSkills()?.GetSkillDef(skill)?.m_icon;
            GameObject iconObject = Ui("SkillIcon", panel.transform);
            RectTransform ir = iconObject.AddComponent<RectTransform>();
            ir.anchorMin = ir.anchorMax = new Vector2(.5f, .5f);
            ir.anchoredPosition = customArtwork ? new Vector2(0f, 11f) : new Vector2(0f, 34f);
            ir.sizeDelta = customArtwork ? new Vector2(270f, 163f) : new Vector2(132f, 132f);
            Image image = iconObject.AddComponent<Image>(); image.sprite = icon; image.preserveAspect = true; image.color = Color.white;
            if (!customArtwork)
            {
                Outline iconOutline = iconObject.AddComponent<Outline>(); iconOutline.effectColor = accent; iconOutline.effectDistance = new Vector2(3f, -3f);
            }

            string skillName = PerkLocalization.Localize("$skill_" + skill.ToString().ToLowerInvariant());
            if (customArtwork)
            {
                Color textColor = new Color(.95f, .90f, .78f);
                AddText(panel.transform, skillName + " · " + milestone, new Vector2(0f, 105.5f), 16f, textColor);
                AddText(panel.transform, PerkLocalization.Localize(perk.NameToken), new Vector2(0f, -94f), 22f, textColor);
            }
            else
            {
                AddText(panel.transform, milestone >= 100 ? "ЗОЛОТА МАЙСТЕРНІСТЬ" : milestone >= 70 ? "СРІБНА МАЙСТЕРНІСТЬ" : "БРОНЗОВА МАЙСТЕРНІСТЬ", new Vector2(0f, 122f), 30f, accent);
                AddText(panel.transform, PerkLocalization.Localize(perk.NameToken), new Vector2(0f, -75f), 38f, accent);
                AddText(panel.transform, skillName, new Vector2(0f, -122f), 23f, new Color(.88f, .85f, .77f));
            }

            MasteryMilestoneAnimator animation = _active.AddComponent<MasteryMilestoneAnimator>();
            animation.Initialize(player, panel.transform, group, Duration);

            AchievementUnlockPopup template = Achievements.m_instance?.m_unlockAchievementPopup?.GetComponent<AchievementUnlockPopup>();
            if (achievementSound && MasteryPlugin.Settings.EnablePerkSFX.Value && template?.m_unlockSfx != null)
            {
                PerkAudioService.PlayPrefab("milestone_achievement", template.m_unlockSfx, player.GetCenterPoint(), 0.5f);
            }
        }

        private static GameObject Ui(string name, Transform parent) { GameObject go = new GameObject(name); go.transform.SetParent(parent, false); return go; }
        private static void AddText(Transform parent, string value, Vector2 position, float size, Color color)
        {
            GameObject go = Ui("Text", parent); RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(520f, 52f);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size; text.color = color;
            text.alignment = TextAlignmentOptions.Center; text.fontStyle = FontStyles.Bold;
            text.enableAutoSizing = true; text.fontSizeMin = size * .65f; text.fontSizeMax = size;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis;
            if (MessageHud.instance?.m_messageCenterText?.font != null) text.font = MessageHud.instance.m_messageCenterText.font;
        }
    }

    internal sealed class MasteryMilestoneAnimator : MonoBehaviour
    {
        private Player _player; private Transform _panel; private CanvasGroup _group; private float _start; private float _duration;
        private GameObject[] _effects;
        private Vector3[] _scales;
        internal void Initialize(Player player, Transform panel, CanvasGroup group, float duration)
        {
            _player = player; _panel = panel; _group = group; _duration = duration; _start = Time.unscaledTime;
            panel.gameObject.SetActive(MasteryPlugin.Settings.EnableMilestoneMessages.Value);
            List<GameObject> visuals = new List<GameObject>();
            if (player.m_skillLevelupEffects?.m_effectPrefabs != null)
                foreach (EffectList.EffectData effect in player.m_skillLevelupEffects.m_effectPrefabs)
                {
                    if (effect == null || !effect.m_enabled || effect.m_prefab == null) continue;
                    PerkAudioService.PlayPrefab("milestone_level", effect.m_prefab, player.GetCenterPoint(), 0.5f);
                    if (!MasteryPlugin.Settings.EnableMilestoneVFX.Value) continue;
                    GameObject visual = PerkNativeFeedback.CreateVisualOnly(effect.m_prefab);
                    if (visual == null) continue;
                    visual.transform.SetParent(player.transform, false);
                    visual.transform.position = player.transform.position + Vector3.up * .7f;
                    visual.transform.localScale *= 5f;
                    visuals.Add(visual);
                }
            _effects = visuals.ToArray();
            _scales = new Vector3[_effects.Length];
            for (int i = 0; i < _effects.Length; i++)
            {
                if (_effects[i] == null) continue;
                _scales[i] = _effects[i].transform.localScale;
                foreach (ParticleSystem particles in _effects[i].GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particles.main;
                    main.loop = false;
                    main.startLifetime = duration;
                    main.stopAction = ParticleSystemStopAction.None;
                }
                _effects[i].SetActive(true);
                GameObject readyEffect = _effects[i];
                NativeVfxSafeFrame.AfterReady(readyEffect, () =>
                {
                    if (readyEffect == null) return;
                    foreach (ParticleSystem particles in readyEffect.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                        particles.Play(false);
                    }
                });
            }
        }
        private void Update()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _start) / _duration);
            if (_panel != null) _panel.localScale = Vector3.one * Mathf.Lerp(1.18f, .72f, t);
            if (_group != null) _group.alpha = t < .62f ? 1f : 1f - (t - .62f) / .38f;
            if (_effects != null)
                for (int i = 0; i < _effects.Length; i++)
                    if (_effects[i] != null) _effects[i].transform.localScale = _scales[i] * (1f - t);
            if (t >= 1f || _player == null || _player != Player.m_localPlayer) Destroy(gameObject);
        }
        private void OnDestroy()
        {
            if (_effects != null) foreach (GameObject effect in _effects) if (effect != null) Destroy(effect);
        }
    }
}
