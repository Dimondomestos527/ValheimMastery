# COMBAT100_FAVOR_DESIGN_HANDOFF_20261005
- Owner/profile: COMBAT_100; .agent/profiles/COMBAT_100.md. SIMPLE root-only routing task.
- Human authorization2026-10-05: attached Favor systems-design prompt; user explicitly permits forwarding Pantheon scope to its owner or providing manual copy. Recipient uniquely verified: Mastery — Gold Core / Pantheon, thread01a1087b-f425-7c53-b561-8c1bb30f18b8, local, same project/cwd.
- Goal: deliver full Favor economy brief to GOLD_CORE with current policy/Combat100 boundaries and preserve exact original prompt.
- Acceptance: original prompt preserved; recipient/source scope verified; numerical conflict resolved by direct user reply; successful send acknowledgment; matching task/progress/handoff readback. Economy design completion belongs recipient's separate task.
- Exact write reservations: .agent/tasks/combat100/COMBAT100_FAVOR_DESIGN_HANDOFF_20261005.md; .agent/progress/COMBAT100_FAVOR_DESIGN_HANDOFF_20261005.md; .agent/handoffs/COMBAT100_FAVOR_DESIGN_HANDOFF_20261005.md. Only these3 docs; no overlapping reservation.
- Relevant docs/source inspected: AGENTS/profile/bootstrap; PANTHEON_CORE; GOLD_CORE profile; GOLD_CORE_COST_TIER_COOLDOWN_20261005 handoff; GoldCooldownPolicy.cs; GoldCraftingService/FavorService targeted source. Ideation context located at docs/exports/VALHEIM_MASTERY_IDEATION_CONTEXT.md for recipient. Combat100 prior task/handoff read.
- Shared systems touched NONE (read-only contract inspection and design routing). No gameplay/build/runtime/ledger/persistence/UI mutations. No new chat/agents/deploy/launch/release/external memory.
- Required checks: brief/recipient/source fidelity and record readback only; no build or gameplay tests.
- Complexity SIMPLE; root-only. Existing destination is user's persistent owner chat, not a spawned agent. Native scout unnecessary for routing.
- Known dependencies: later Gold/Utility source work observed; receiving owner must use latest canonical files and not restore older snapshot. Action owners retain actual ability effects; GOLD_CORE owns shared economy/protocol.
- Status COMPLETE routing scope; reservations RELEASED. Favor economy design remains recipient work, not completed by this task.

## Resolved current design constraints
Human clarification2026-10-05: «Зберегти сьогоднішній погоджений контракт». It overrides stale cooldown clauses in the attached prompt:
- cost<=250 Favor:0sec;250<cost<=500:300sec;cost>500:1200sec.
- Any active source-patron A cooldown blocks all A actions, including cheap ones; other patrons B blocked only if cost>250. Allowed B<=250 action never resets/extends A timer.
- Patron-specific Favor balances/scales per individual player/world; all associated skills of one patron fill/spend its single shared pool. No common cross-patron wallet. Real multi-patron runtime not established by this design; legacy Völundr scalar is not another patron wallet.
- Attached original used different250/500 boundaries and major-only foreign>=500 restriction; preserve original for audit, but use clarified rules for final model/simulation.
- Combat100 baseline2026-10-04 found11 CATALOG ONLY slots and0 dedicated execution paths. Not a new live/current drift audit. Future750-cost ability descriptions are design inputs, not implemented source evidence.
- Whole incoming brief addresses shared Favor economy/pacing/gauge/anti-farm/attribution: route whole prompt; do not split shared design into duplicate Combat100 implementation.
- Implementation remains outside this request. Receiving owner creates own task/progress/handoff, checks current evidence, produces requested design and explicit model assumptions; action costs/effects already approved not redesigned.

## Original user prompt — preserved verbatim; clarified cooldown above takes precedence

Ти — окремий systems-design chat для Valheim Mastery.

Твоя задача — спроектувати:

1. Favor economy;
2. способи отримання Favor;
3. Favor gauge / UX;
4. pacing між Gold abilities;
5. anti-farming / anti-exploit rules;
6. solo/co-op semantics.

НЕ проектуй заново конкретні Gold abilities і не змінюй уже затверджені perks. Favor — окрема shared progression/economy system.

Перед аналізом ознайомся з:

- `VALHEIM_MASTERY_IDEATION_CONTEXT.md`
- current Gold Core / Pantheon design
- current Favor ledger / exhaustion implementation
- Gold acknowledgement
- Combat/Magic/Utility 100 structure
- XP/progression systems
- first-time/catch-up mechanics
- boss/world progression mapping
- generated-hit provenance
- gathering/crafting/processing attribution
- anti-farming concepts
- multiplayer ownership/reward attribution
- HUD/UI framework

Canonical current source/docs мають пріоритет над snapshot.

---

# CURRENT DESIGN CONSTRAINTS

Favor — ресурс для Gold / level-100 divine abilities.

У поточному design вже існують abilities із дуже різною вартістю, включно з major powers приблизно по 750 Favor.

Shared Gold cooldown rule:

- cost <250 Favor → no Gold cooldown;
- cost 250–499 → 5-minute Gold cooldown;
- cost >=500 → 20-minute Gold cooldown.

Cooldown ability:

- блокує всі Gold abilities того самого patron;
- якщо це major cooldown, також блокує abilities інших patrons із cost >=500;
- cheaper abilities інших patrons можуть залишатися доступними.

Favor economy повинна працювати разом із cooldown system, а не дублювати її.

Не роби Favor настільки рідкісним, що Gold abilities “бережуть до фінальних титрів”.

Не роби Favor настільки дешевим, що cost перестає бути decision.

---

# MAIN DESIGN QUESTION

Favor має відповідати на питання:

> “Що повинен робити сильний, досвідчений Viking у звичайному Valheim gameplay, щоб заслужити право частіше звертатися до богів?”

Відповідь не повинна бути:

> “фармити один найдешевший моб 400 разів.”

Favor gain має заохочувати нормальний Valheim loop:

- exploration;
- combat;
- risk;
- bosses;
- difficult enemies;
- new biomes;
- professions;
- preparation;
- co-op;
- meaningful accomplishments.

Але не перетворювати гру на daily quests, MMO reputation grind або окрему валютну ферму.

---

# FAVOR SCALE

Спочатку оцінити, чи поточний conceptual maximum Favor = 1000 є хорошим cap.

Current source може містити Favor cap 1000, але не трактуй його автоматично як незмінне balance decision.

Проаналізуй:

- чи достатньо 0–1000 для хорошого UX;
- як часто player повинен реально бачити +Favor;
- скільки часу/подій приблизно має коштувати ability 100 / 250 / 500 / 750;
- чи повинен player мати можливість накопичити Favor на одну major ability і ще трохи залишити на cheaper ability;
- чи потрібен soft cap або тільки hard cap;
- що відбувається з Favor gain біля cap;
- чи повинен Favor переноситися між sessions/worlds/characters відповідно до current Gold Core semantics.

Не міняй persistence model без architecture review.

---

# FAVOR SOURCES

Створи кілька категорій отримання Favor.

Для кожної категорії оціни:

- gameplay value;
- repeatability;
- farming risk;
- solo/co-op behavior;
- attribution;
- expected Favor/hour;
- whether diminishing returns or first-time bonus is needed.

Обов'язково досліди принаймні:

### 1. Combat accomplishments

Не просто Favor per kill.

Розглянь:

- enemy tier;
- biome tier;
- starred enemies;
- dangerous encounters;
- enemies substantially stronger than player progression;
- elite/miniboss-like enemies;
- boss kills;
- repeated boss kills versus first kill;
- multi-enemy combat;
- parry/dodge/mastery actions тільки якщо їх неможливо легко farm;
- killing blows versus meaningful participation.

Generated secondary hits не повинні створювати duplicate Favor.

### 2. Exploration

Розглянь Favor за:

- discovering new biome;
- first arrival in dangerous biome;
- discovering important locations;
- dungeons;
- rare world structures;
- boss locations;
- major exploration milestones.

Не нагороджуй безкінечно за ходіння між двома точками.

### 3. Boss / Forsaken progression

Boss events можуть бути одним із найбільш “divine” джерел Favor.

Проаналізуй:

- first kill reward;
- repeat kill reward;
- group participation;
- whether summoning cost / repeated farming makes abuse possible;
- reward scaling across bosses.

### 4. Professions / peaceful play

Gold economy не повинна змушувати Cooking/Crafting/Farming/Mining-oriented player постійно йти вбивати мобів лише для Favor.

Але не давай Favor за кожну морквину.

Розглянь meaningful milestones:

- valuable production;
- difficult/advanced recipes;
- large processing jobs;
- resource tier progression;
- first-time crafted items;
- exceptional outcomes;
- building/crafting accomplishments;
- profession-specific high-value events.

Враховуй existing producer/collector attribution проблеми.

### 5. Risk / survival accomplishments

Розглянь, але дуже критично:

- surviving at low HP;
- long expeditions;
- returning with valuable cargo;
- fighting far from rested/safe zones;
- dangerous weather/biome states.

Відкинь усе, що легко штучно farm через self-damage, standing in hazards або scripted loops.

### 6. Co-op

Favor не повинен змушувати команду конкурувати за last hit.

Розроби semantics для:

- shared boss kills;
- shared difficult encounters;
- support contribution;
- nearby participation;
- profession collaboration.

Водночас один player не повинен стояти AFK у base і отримувати Favor за всю карту.

---

# FAVOR GAIN PHILOSOPHY

Розглянь hybrid economy:

- small repeatable Favor;
- larger meaningful-event Favor;
- first-time / discovery spikes;
- rare large rewards.

Не всі gameplay actions повинні давати Favor.

Favor notification має залишатися meaningful.

Продумай diminishing returns або anti-farm тільки там, де вони реально потрібні.

Не додавай hidden complexity заради самого anti-exploit.

---

# FAVOR GAUGE UX

Спроектуй Favor gauge.

Вона має бути:

- vanilla-readable;
- компактна;
- не MMO resource bar, що постійно кричить на HUD;
- легко зрозуміла перед використанням Gold ability;
- корисна навіть коли Favor майже full.

Розглянь:

- постійна versus contextual visibility;
- число `Favor / Max`;
- segmented або continuous gauge;
- major thresholds;
- indication, що конкретна Gold ability доступна;
- insufficient Favor feedback;
- Favor gain animation;
- Favor spend animation;
- cap indication;
- cooldown interaction.

Продумай, чи варто показувати маркери на:

- 250;
- 500;
- 750;
- або thresholds повинні залежати від equipped/available abilities.

Не створюй HUD-калькулятор на пів екрана.

---

# PRESENTATION

Favor повинен відчуватися як прихильність богів, а не як “Mana 2”.

Розроби presentation intent для:

- small Favor gain;
- large Favor gain;
- reaching cap;
- spending Favor;
- insufficient Favor;
- unlocking first Gold ability.

Тільки vanilla Valheim assets / Unity-native composition.

Exact prefab/audio names не вигадувати.

---

# ECONOMY STRESS TEST

Обов'язково перевір сценарії:

1. New level-100 player.
2. Endgame solo player.
3. 4-player co-op.
4. Boss farming.
5. Greyling/low-tier mob farming.
6. Base profession farming.
7. AFK teammate.
8. Player at Favor cap.
9. Player repeatedly using cheap <250 abilities.
10. Player saving for 750-cost major ability.
11. Player після використання 750 Favor із 20-minute cooldown.
12. Multiple patrons unlocked.
13. Moving between worlds, якщо current Gold Core це дозволяє.
14. Death/reconnect/restart.

---

# OUTPUT FORMAT

Спочатку дай короткий design thesis.

Потім:

## FAVOR SCALE
- cap
- target accumulation speed
- expected costs relative to gameplay

## FAVOR SOURCES
Таблиця:

Source | Favor amount/range | Repeatability | Anti-farm | Solo | Co-op | Why it exists

## FAVOR FORMULA / RULES
Якщо пропонується formula, вона повинна бути простою й пояснюваною гравцю.

## FIRST-TIME / MAJOR REWARDS

## PROFESSION FAVOR

## CO-OP ATTRIBUTION

## ANTI-EXPLOIT

## FAVOR GAUGE UX

## VFX/SFX INTENT

## EXAMPLE PLAYER SESSION
Покажи приблизно, скільки Favor отримує player за нормальну 1-hour session у кількох різних стилях гри.

## ECONOMY SIMULATION
Оціни, скільки major 750-Favor abilities реально можна використати за 2–3 години нормальної гри з урахуванням 20-minute cooldown.

## RISKS

## RECOMMENDED FINAL MODEL

Для кожної пропозиції чітко позначай:

- FACT FROM CURRENT PROJECT
- FACT FROM VALHEIM / DEVELOPER SOURCE
- GENERAL GAME DESIGN PRINCIPLE
- DESIGN SUGGESTION
- TECHNICAL ASSUMPTION

Не переходь до implementation plan, доки economy/design model не сформований.

## Delivery/readback evidence
Full original prompt plus clarified context sent through send_message_to_thread to01a1087b-f425-7c53-b561-8c1bb30f18b8; successful tool acknowledgment returned same threadId. One bounded wait_threads snapshot confirmed recipient active, latest turn01a10beb-1354-7090-8ae7-b0ebcf62e93e inProgress. No economy completion or future notification promised. Original attachment preserved verbatim below heading; task/progress/handoff readback verified. Shared/source/runtime changes NONE by this routing task.

