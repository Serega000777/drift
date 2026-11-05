# Установка и первый запуск в Unity (Windows / macOS)

## 1. Что поставить
- **Unity Hub** и **Unity 6 LTS** (6000.x). Модули: **Android Build Support** (SDK/NDK/OpenJDK), позже iOS (только на Mac).
- Git и **Git LFS** (`git lfs install` один раз).
- Для тестов без Unity (необязательно): .NET 8 SDK → `dotnet test tools/CoreTests`.

## 2. Почему нужен шаг с шаблоном
В репозитории лежит только наш код и данные (`UnityProject/Assets/_DriftCareer`). Версии пакетов Unity (URP, Input System) зависят от вашей версии редактора, поэтому `Packages/` и `ProjectSettings/` берём из официального шаблона — так версии гарантированно совместимы.

## 3. Шаги
1. Unity Hub → **New project** → шаблон **Universal 3D** → имя `DriftTemplate` → в любой папке **вне** репозитория → Create. Дождитесь открытия и закройте Unity.
2. Скопируйте из `DriftTemplate` в `<репозиторий>/UnityProject/`:
   - папку `Packages/`
   - папку `ProjectSettings/`
   - (по желанию) `Assets/Settings/` из шаблона — там готовые URP-ассеты.
   Скрипт сделает это сам:
   - Windows (PowerShell): `./tools/setup/adopt-unity-template.ps1 -Template "C:\path\DriftTemplate"`
   - macOS/Linux: `./tools/setup/adopt-unity-template.sh /path/DriftTemplate`
3. Unity Hub → **Add** → **Add project from disk** → выберите `<репозиторий>/UnityProject`. Откройте.
4. Если Unity спросит про **новую систему ввода** — «Yes» (проект поддерживает и старую, и новую, и «Both»).
5. Меню **Drift Career → 1. Setup Project** — создаст `Assets/_DriftCareer/Scenes/Bootstrap.unity`, добавит её в Build Settings, включит Linear color space, ориентацию landscape и добавит шейдеры в Always Included.
6. **Drift Career → 2. Setup URP Graphics** — HDR, тени, SSAO.
7. **Drift Career → 3. Validate Project** — все пункты должны быть OK.
8. Откройте `Bootstrap.unity` → **Play**.

## 4. Что должно произойти
- Заставка «Нажми, чтобы начать» поверх ангара с машиной (камера медленно облетает).
- Любая клавиша/тап → карусель карточек: «Тестовый полигон», «Тренировки», режимы, «Гараж», остальное — «Скоро».
- «Тестовый полигон» → интро (проход по ангару, посадка, запуск мотора) → автовыезд → управление ваше (любое нажатие газа/руля перехватывает).

## 5. Если что-то не так
| Симптом | Что сделать |
|---|---|
| Ошибки компиляции в `Runtime`/`Editor` | Скопируйте текст ошибки ИИ-агенту: эти ветки (Unity 6 / URP) не компилировались вне Unity. |
| Розовые материалы | URP не назначен: выполните «2. Setup URP Graphics» или назначьте URP-ассет в Project Settings → Graphics. |
| Нет текста в UI | Проверьте, что `Resources/DriftCareer/UI/DriftTheme.tss` импортирован (Validate покажет). |
| Машина не едет с клавиатуры | Project Settings → Player → Active Input Handling = Input System Package или Both. |
| Две камеры / двойной свет | Bootstrap выключает камеры сцены; используйте пустую `Bootstrap.unity`. |
| Хочу сбросить прогресс | Drift Career → Delete Local Saves. |

## 6. Сборка на Android
File → Build Profiles → Android → Switch Platform → Player: Scripting Backend IL2CPP, ARM64, Minimum API 26+. Build And Run на телефоне с включённой отладкой по USB. Замеры FPS/нагрева записывайте в `docs/TEST_PLAN.md`.

## 7. Работа на Mac M2 8 ГБ (из исходного ТЗ)
Закройте тяжёлые приложения, Game view в низком разрешении, качество «Medium», не держите открытыми Blender/браузер. Проект процедурный и лёгкий — импорт должен занимать несколько минут.
