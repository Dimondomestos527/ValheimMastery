# UX_COMBAT100_DIVINE_ASSET_RAVEN_AUDIT_20261006 handoff
- Status: COMPLETE read-only audit/design. Full evidence and options: `validation/ux-combat100-divine-assets-raven-20261006/REPORT_UA.md`.
- User-rejected sparks/perfectblock/custom-orbit visuals remain rejected; do not revive them as fallbacks.
- Recommended first auditions: Týr A (`fx_Adrenaline1` visual-only + two stripped BloodGold trinket emitters + optional boss-stone completion SFX) and Odin A (full actual GlobalWind/EnvMan emitter + `vfx_odin_despawn` + restrained atgeir identification cue).
- Critical implementation boundary: never play `fx_Adrenaline1` built-in audio; it contains shield-generator startup and fire-ignite cues. Never instantiate a trinket item prefab or copy only the wind material. Clone/sanitize visual components only.
- Static evidence: exact SoftRef IDs/paths and serialized timing/color/component metadata recorded. Runtime ZNetScene resolution and live look/sound remain AUDITION REQUIRED. Exact GlobalWind object/emitter name remains runtime UNKNOWN.
- Raven: preview does not call Unlock; native queue is exact-key deduplicated FIFO. Observed Polearms lesson after Fists preview is consistent with older pending history, not proof of skill remapping. No queue repair or history clearing justified.
- Reservation: this audit held only task/progress/handoff/report files. `MasteryRavenTutorials.cs` remains under the prior UX presentation-gate reservation; no canonical source change was made.
- Root next step: obtain user's meaningful choice after live isolated auditions; only then reserve/integrate the selected presentation. Keep 3–5 s duration, yellow recognition line, no second frame, no Favor claims.
