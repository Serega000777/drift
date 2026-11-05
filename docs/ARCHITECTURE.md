# Архитектура Drift Career

## 1. Слои
```
┌─────────────────────────────── DriftCareer.Editor (только редактор) ───────────────────────────────┐
│ Меню Setup/Validate, настройка URP, Release Hardening, импорт своих 3D-моделей авто и персонажей   │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────── DriftCareer.Runtime (Unity) ────────────────────────────────────────┐
│ Bootstrap: GameBootstrap → GameServices (композиционный корень) → GameController (поток состояний)  │
│ Vehicle: VehicleController (Rigidbody+лучи) · CarRig · VehicleEffects · DamageDeformer · Audio      │
│ Track: AcademyBuilder · TrackSession · MaterialLibrary · ProceduralMeshes · SurfaceTag              │
│ Input: InputRouter (Input System / legacy / тач / наклон) · Keys                                    │
│ Cameras: CameraRig · IntroDirector     LayoutEditor: ObstacleField · LayoutEditorController         │
│ Gameplay: DriftSession · GhostCar      UI: UiRoot · UiKit · экраны (меню, HUD, гараж, кабинет, …)   │
│ UI: OnboardingScreen · LoadingScreen · TransitionOverlay    Character: CharacterRig                 │
│ Security: DeviceKeyProvider · SafeLog                                                               │
│ GraphicsManager (URP: тени, AA, постобработка, пробы)                                               │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────── DriftCareer.Core (чистый C#, noEngineReferences) ───────────────────┐
│ Vehicle: TireModel · Suspension/Steering · VehicleSimulation (трансмиссия) · VehicleHealth          │
│          VehiclePhysicsProfile · VehicleSpecSheet→профиль · PlanarVehicleHarness (тесты)            │
│ Assists · Gameplay (очки, упражнения, режимы, турниры, испытания, экономика, призраки, тандем)       │
│ Garage (тюнинг, визуал, свапы, корч, оценка характеристик, свои авто, ИИ-интерфейс)                 │
│ Catalog · Localization · Persistence (версионированные документы) · Layout (редактор) · Surfaces    │
│ Platform (уровни устройств, IPlatformServices) · Account (кросс-прогрессия, политики, Auth,         │
│          Privacy, PlayerIdentity) · Security (Crypto, SecureStore, подпись, LogRedactor) · Character │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
```
Правило зависимостей: Editor → Runtime → Core. Core ничего не знает про Unity и полностью тестируется `dotnet test`.

## 2. Сцена и поток
Одна бесшовная сцена: процедурная Академия строится при старте (корутина `BootRoutine` → `TrackSession.BuildAcademyRoutine` → `AcademyBuilder.BuildRoutine`, прогресс по этапам на `LoadingScreen`) и служит фоном меню, гаража и кабинета (камера-шоурум), поэтому дальше меню → гараж → езда без загрузок, только «шторка» `TransitionOverlay.Wipe`. Первый запуск: загрузка → `OnboardingScreen` (возраст, согласия, ник/вход) → меню. Состояния (`GameFlow`): Boot → MainMenu ⇄ Garage/Settings → Loading → Intro → Driving ⇄ Paused/LayoutEditor; недопустимые переходы отклоняются (тест).

Новые трассы подключаются через `TrackDefinition.loaderId`: сейчас `procedural:academy`; дальше `scene:<имя>` или `addressable:<ключ>`. Будущий контент — только данные в `catalog.json` (`ComingSoon`), без пустых сцен.

## 3. Физика (подробно — PHYSICS_SPEC.md)
`VehicleController.FixedUpdate` (0.01 с): ввод → ассисты → `Sim.Prepare` (руль, КПП, сцепление) → лучи колёс + подвеска → скорости пятен в повёрнутых осях колёс → `Sim.Step` (решатель трансмиссии, силы шин) → `AddForceAtPosition` → аэро → `VehicleHealth.Update` (температура/износ/разрывы → модификаторы колёс) → телеметрия. Визуал читает физику в `LateUpdate`.

## 4. Данные и сохранения
- Каталог, локализация, стили — текстовые файлы в `Resources/DriftCareer` (диффабельны, удобны для ИИ).
- Сохранения: `IFileStore` (локально `persistentDataPath/DriftCareer`; консоли — реализация поверх их save API) + `VersionedDocumentStore<T>`: конверт `{schema, version, crc, data}`, атомарная запись (tmp → bak → замена), загрузка main → tmp → bak → default, битые файлы в карантин `.corrupt-*`, миграции в кодеках. Явный маппинг JSON без рефлексии.
- Документы: `settings.json`, `profile.json` (тюнинг, сетапы, кошелёк с историей, детали, машины, испытания, рекорды, ник, персонаж), `privacy.json`, `layouts/*.json`, `ghosts/*.json`; секреты — `secure/*.sec` (зашифрованы).
- Подпись: конверт получает поле `sig` = HMAC(schema|version|data) ключом установки (`ISaveSigner`). Неверная подпись → документ всё равно загружается, но `LoadReport.tampered` → `PlayerProfile.integrityCompromised` (навсегда) → соревновательное выключено.

## 5. Платформы: мобилки → ПК → консоли
| Что | Мобилки (первые) | ПК / Steam | Консоли |
|---|---|---|---|
| Ввод | тач (руль/слайдер/кнопки), наклон, геймпад | клавиатура, геймпад, руль (позже, FFB) | геймпад, навигация фокусом |
| Качество | Low/Medium по `DeviceTierClassifier`, 30/60 FPS | High/Ultra, 120 FPS | High, фикс. 60 |
| Свои модели | нет (только через модерацию) | редакторный импорт | нет |
| Магазин | Google Play / App Store | Steam | PSN / Xbox / Nintendo |
Платформенные различия — только за интерфейсами (`IPlatformServices`, `IFileStore`, `IAccountService`, `PlatformPolicy`). Геймплей и физика одни.

## 6. Единый аккаунт (кросс-прогрессия)
- `AccountState`: один `accountId`, привязанные идентичности магазинов (`LinkedIdentity`).
- **Заработанное** (кредиты, детали, машины, рекорды) — общее на всех платформах.
- **Купленное за реальные деньги** — `Entitlement.purchasedIn` + `paidCurrency[store]`: тратится там, где куплено, если политика магазина не разрешает перенос (`PlatformPolicy.paidCurrencyCrossPlatform`).
- Ограничения платформ (`PlatformPolicy`): импорт моделей только ПК, текстовый чат выкл. на консолях, внешние ссылки только ПК, обязательный вход в аккаунт платформы на консолях. **Перед сертификацией сверить с текущими соглашениями платформодержателей.**
- Синхронизация: сервер авторитетен; офлайн-изменения сливаются детерминированно (`AccountState.Merge`: права — объединение, рекорды — максимум, кредиты — серверное значение + локальная дельта). Сейчас `LocalAccountService` (сервера нет).

## 7. Онлайн-тандем (план)
Сейчас: офлайн-тандем с призраком (`GhostRecording` 20 Гц, `TandemScoring`). Реальные игроки — `ITandemTransport`:
1. Лобби/матчмейкинг (Unity Lobby/Relay или свой сервис на VPS).
2. Каждый клиент симулирует свою машину; рассылает снапшоты 20–30 Гц (поза, скорость, угол, руль); чужая машина интерполируется с задержкой ~100 мс, экстраполяция до 200 мс.
3. Счёт тандема считается обоими клиентами, финальный — на сервере по присланным записям (ghost) + проверка правдоподобия физикой ядра (анти-чит: то же ядро без Unity).
4. Кросс-плей с учётом `PlatformPolicy.allowCrossPlay`.

## 8. Свои автомобили и ИИ
- ПК/редактор: `CustomCarImporter` → `ModelAnalysis` (колёса, база, колея, габариты, треугольники) → `VehicleSpecSheet` → манифест + префаб в `Resources/DriftCareer/UserCars`. Физика из спеков, модель — только визуал.
- ИИ-распознавание: `IVehicleIdentificationService`. Продакшн — серверный эндпоинт: рендеры/фото → vision-модель → кандидаты (марка/кузов/привод/КПП) → лицензированная база спецификаций → `SpecCandidate` с уверенностью и источниками. Игрок подтверждает. Ключи API никогда не в клиенте. Офлайн-фолбэк — `GeometryOnlyIdentificationService` (архетипы по геометрии).
- Публичный UGC: FBX/GLB + манифест + подтверждение прав → карантин → скан → валидатор геометрии → модерация → сборка платформенных ассетов доверенным пайплайном → подписанный каталог → жалобы/снятие. Никаких AssetBundle и скриптов от игроков.

## 9. Экономика
Только мягкая валюта (кредиты) за игру: серии, упражнения, испытания, турниры. Цены деталей пропорциональны влиянию. Всё, что влияет на управление, зарабатывается игрой (без pay-to-win). Платная валюта/косметика — будущее, за `IPlatformServices`/магазином, с привязкой к платформе.

## 10. Графика
URP: HDR, мягкие тени (дистанция/каскады по уровню), SSAO (renderer feature), FXAA/SMAA/MSAA, глобальный volume (ACES, bloom, цветокор, виньетка, motion blur, зерно — всё переключаемо), clear coat на кузове (Complex Lit), карты нормалей для асфальта/бетона, reflection probes в ангаре и над площадкой, туман и процедурное небо. Уровни Low → Ultra меняют тени, render scale, частицы, пробы, бюджет препятствий.

## 11. Безопасность, вход, приватность (подробно — `SECURITY_PRIVACY.md`, сервер — `BACKEND_API.md`)
```
GameServices
 ├─ Keys: IKeyProvider (DeviceKeyProvider) ──► DeriveKey("saves") → HmacSaveSigner → VersionedDocumentStore(sig)
 │                                         └► DeriveKey("secure") → EncryptedFileSecureStore (ISecureStore)
 ├─ Auth: AuthService(IAuthBackend, ISecureStore) ◄── IAuthProvider (платформа даёт одноразовый пропуск)
 ├─ Privacy: PrivacySettings (privacy.json) ─┐
 ├─ Policy: PlatformPolicy ──────────────────┼─► PrivacyRules.Resolve → Capabilities (что можно этому игроку)
 └─ Profile.integrityCompromised ────────────┘
```
- Клиент недоверенный: подпись — от случайной правки; авторитетны только сервер и его проверки (то же ядро Core).
- Все логи — через `SafeLog` (маскировка). Ник — `PlayerIdentity.Validate` (без персональных данных).
- Платформенные хранилища (Keychain/Keystore/DPAPI/консоли) подключаются реализациями `ISecureStore`/`IKeyProvider` без изменения остального кода.

## 12. Личный кабинет и персонаж
- `ProfileScreen`: обзор, пилот, рекорды, кошелёк, аккаунт, приватность, безопасность. Камера-шоурум переключается между машиной и пилотом (`GameController.FocusPilot`).
- `CharacterAppearance` (Core, данные) → `CharacterRig` (Runtime, процедурная модель из примитивов, поза стоя/сидя). Экипировка — `CharacterCatalog` (слоты, цены в кредитах, только косметика). Своя модель — `CustomCharacterImporter` (ПК), подключается через `UseCustomModel`, если `PlatformPolicy.allowUserModelImport` и `Capabilities.userModelImport`.
