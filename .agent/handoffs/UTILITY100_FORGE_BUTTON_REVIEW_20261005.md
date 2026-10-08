# Utility100 owner review — Forge button

## Final button amendment — accepted

2026-10-05: bounded compatibility SOURCE REVIEW APPROVED для GoldMasterworkButton.cs SHA256 C2953A84DCEAF01A81502D311D7EE98A915A2FE8D079793D1D57F18C67567E3E. Supersedes button hash below only. GDT BE5B76C0DAAD3C097FD451857FE2A62AB5D509C1C35A6224ED397EBDC1541037 та rules353F72D216BAAD7A938D67ADBB5FC5D6D4EDA86795FC138AAC04FE80E2371FAE залишаються точними попередньо схваленими версіями.

Перечитано весь final button: localized Forge label/tooltip точно вказують250/+3/required idol/Hammer; dispatcher BeginForge/BeginMasterwork та CanStart eligibility не змінені. ReleaseFocus діє тільки коли вибрана власна кнопка або її дочірній елемент; перед hiding/destroy переносить selection на active/interactable native craft button або очищає його. SetVisible(false) та OnDestroy викликають cleanup; CancelMasterwork збережено. Не викликає native craft/Gold action або списання при зміні focus. Нових блокерів у межах domain compatibility не знайдено. Controller navigation/event ordering і rendered layout залишаються LIVE-unverified;11 UX checks повідомлені Gold, Utility їх не запускав.

Review-only amendment, жодних craft source writes/reservations з боку Utility. Gold root може виконувати власну серійну інтеграцію після final combined builds/drift checks за своєю активною задачею. Встановлення не дозволялося і не виконувалося.

COMPLETE / BOUNDED SOURCE REVIEW APPROVED. Exact review-document reservations RELEASED. Root read-only review; no domain source/candidate changes, builds, installation or production state writes. Gold root retains final integration ownership; Utility has no overlapping craft-file write reservation.

Reviewed candidate validation/gold-weapon-key-p-20261005/candidate/:
- GoldDivineTransactions.cs BE5B76C0DAAD3C097FD451857FE2A62AB5D509C1C35A6224ED397EBDC1541037
- GoldMasterworkButton.cs A24575234D334C7FCE96C9309B4BDE95EB74EF5E644784B317FD069A9168C77B
- GoldMasterworkRules.cs 353F72D216BAAD7A938D67ADBB5FC5D6D4EDA86795FC138AAC04FE80E2371FAE

Вузьких блокерів у переході Armed/F7 → явна station button не знайдено. Кнопка на updater station обирає BeginForge; звичайні Start/Intercept без власного intent пропускають native craft. Під час активного intent звичайна кнопка не запускає другу операцію. Kind2 server policy зберігає Hammer/HasGold, але прибирає Armed; Kind1 не отримує вимоги молота.

CanStartForge читає фактичний selected inventory item і prefab, required inventory idol/quality, readiness250, unresolved receipt/Workshop work та native station usability. Preflight не створює GUID. Intent фіксує reference предмета, old quality, item variant окремо від GUI variant, recipe/player/session/station. Refresh/Intercept відкидають підміну на інший предмет навіть тієї самої якості, втрату inventory membership, зміну selection/session/station/death/teleport/disabled; cancel/close скидають timer, тому скасована операція не перетворюється на звичайний craft.

LocalValid з candidate параметром використовується тільки read-only preflight; подальша дія зберігає Target lookup за ItemIdentity. Local/ServerValid залишають upgrader requirement, station identity/distance/environment/ward, належний idol та Hammer. Ціна250, same-item +3, предметний variant, оплата inventory idol, Requested/Applied/Rejected/save/ACK/rollback code не змінені у diff. Native world idols із MasterIdol* — інша система і цією правкою не зачіпаються.

Candidate GoldCraftingService без F7 продовжує періодичний Sync(HoldsHammer); тому ServerHammer не залежить від знятого input handler. Свіжий Hammer snapshot може ще не дійти при дуже швидкому кліку: серверна відмова/повтор після синхронізації є fail-closed, не дозволом обійти Hammer. LIVE latency/hidden-hands перевірка потрібна.

Межі висновку: це не новий аудит усієї Gold persistence/anti-cheat моделі і не LIVE. Збережені старі фінансові/rollback/cross-store ризики не сертифіковано повторно. Refresh intent не перевіряє щокадрово втрату Hammer/idol/ward, але LocalValid/ServerValid знову перевіряють умови до application; за такої втрати прогрес може дійти до безрезультатного завершення. Це UX-обмеження, не підтверджений обхід списання. Working cue повторно використаний; completion flash лишається тільки Kind1, нова Forge completion animation цим запитом не додана.

Gold повідомив dual native builds/provenance та56 extracted actual-code stub assertions PASS; Utility їх не запускав і не видає за власне runtime підтвердження. До інтеграції root: перевірити canonical drift/reservations, актуальні combined-source builds і tests (Kind2 Armed false+Hammer true PASS / noHammer FAIL / unknownKind FAIL), не відновлювати сторонні файли зі snapshot. Після окремого дозволу LIVE: ordinary craft vs explicit Forge, host+remote, hidden Hammer, item/GUI variants, selected-item replacement, interruption/station/ward loss, receipt/reconnect і same-item+3/one-idol/250 exact payment.
