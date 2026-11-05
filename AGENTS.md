# AGENTS.md — правила для ИИ-агентов (Claude Code, Codex, Cursor, Copilot и др.)

Этот файл читают все ИИ-инструменты. Перед любой работой:

1. Прочитай **`AI_DONE.md`** (что уже сделано и как проверено) и **`AI_TODO.md`** (что делать дальше).
2. Для деталей: `docs/ARCHITECTURE.md`, `docs/PHYSICS_SPEC.md`, `docs/TASKS.md`, `docs/DECISIONS.md`, `docs/TEST_PLAN.md`.
3. Исходные требования продукта: `docs/GAME_IDEA.md`, `docs/TECHNICAL_SPEC.md`, `docs/CLAUDE_MASTER_PROMPT.md`.

После своей работы **обязательно обнови `AI_DONE.md` и `AI_TODO.md`** (что сделал, как проверил, что осталось). Это общий журнал для разных ИИ.

## Проект
Drift Career — мобильная (первой) дрифт-игра на Unity 6 + URP, затем ПК (Steam) и консоли. Одна общая симуляция, разные платформенные сервисы.

## Структура
```
UnityProject/Assets/_DriftCareer/
  Core/        DriftCareer.Core      — чистый C# без UnityEngine (физика, правила, данные, сохранения)
  Runtime/     DriftCareer.Runtime   — адаптеры Unity (машина, камера, ввод, мир, UI, графика)
  Editor/      DriftCareer.Editor    — меню «Drift Career», импорт своих моделей
  Resources/DriftCareer/             — каталог контента, локализация RU/EN, USS/TSS интерфейса
  Tests/EditMode                     — NUnit-тесты ядра (также запускаются через dotnet)
  Tests/PlayMode                     — смоук-тесты в реальной физике Unity
tools/CoreTests/                     — dotnet-проект: `dotnet test tools/CoreTests`
```

## Команды
- Тесты ядра без Unity: `dotnet test tools/CoreTests` (нужен .NET 8 SDK).
- В Unity: Window → General → Test Runner → EditMode / PlayMode.
- Меню Unity: **Drift Career → 1. Setup Project**, **2. Setup URP Graphics**, **3. Validate Project**.

## Жёсткие правила
1. `Core` не ссылается на UnityEngine (`noEngineReferences: true`). Всё, что можно протестировать без Unity, — туда.
2. Фиксированный шаг физики 0.01 с. Никогда не крутить машину/не задавать yaw напрямую ради дрифта — только силы в пятнах контакта. Телепорт допустим только для сброса.
3. Ассисты меняют только ввод (руль/газ/тормоз), не физику.
4. Будущий контент — данные в `catalog.json` со статусом `ComingSoon`, а не пустые сцены. «Скоро» нельзя запустить (единая проверка `ContentCatalog.CanLaunch`).
5. Никаких чужих/рипнутых/нелицензированных ассетов, логотипов, брендов. Реальные названия машин/моторов/трасс — только в `referenceNote`/`referenceCode` и показываются в UI лишь с define `DC_SHOW_REFERENCE_NAMES` (dev). Новый ассет → запись в `docs/ASSET_LICENSES.md`.
6. Никаких пользовательских скриптов и AssetBundle от игроков. Свои модели — только через редакторный импорт (ПК) или будущий модерируемый серверный пайплайн.
7. Сохранения — версионированный JSON через `VersionedDocumentStore` (атомарная запись, CRC, бэкап, карантин битых файлов). Без `BinaryFormatter`, без рефлексии.
8. Не утверждай, что что-то проверено в Unity/на телефоне, если не проверял. Пиши честно: «проверено dotnet-тестами», «скомпилировано против reference DLL», «не проверено в редакторе».
9. Не добавляй пакеты без согласования с владельцем. Секреты/ключи API в репозиторий не коммитить (ИИ-распознавание авто — только на сервере).
10. Код — английские идентификаторы, комментарии на английском; документация — на русском.
11. C# 9 (Unity 6): без record, init, file-scoped namespace, global using.
12. Unity 6 API под `#if UNITY_6000_0_OR_NEWER` (linearVelocity, PhysicsMaterial), URP — под `#if DC_URP`, Input System — под `#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM` с legacy-веткой.

## Ветки и коммиты
Рабочая ветка указывается задачей. Коммиты — понятные, по одному смысловому шагу. Перед пушем: `dotnet test tools/CoreTests`.
