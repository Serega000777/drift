# VPS: настройка и автообновление с GitHub

## Схема
```
 ПК с Unity  ⇄  GitHub (главное хранилище)  ⇄  ИИ-агент (пушит код и модели)
                     │
                     ▼  VPS сам забирает каждые 5 минут (ключ только на чтение)
                    VPS: копия проекта + тесты ядра, позже — сервер игры
```
- Пароли и ключи от VPS никому не передаются, в том числе ИИ: сервер сам скачивает обновления.
- 3D-модели и текстуры идут через Git LFS (правила уже в `.gitattributes`).
- Unity-сборку на VPS не делаем: игра собирается на ПК.

## Шаг 1. Войти на VPS
```bash
ssh root@IP_СЕРВЕРА
```

## Шаг 2. Обычный пользователь (если сейчас входите как root)
```bash
adduser drift && usermod -aG sudo drift
```
Если на сервер уже заходите по SSH-ключу, перенесите ключ новому пользователю:
```bash
mkdir -p /home/drift/.ssh && cp ~/.ssh/authorized_keys /home/drift/.ssh/ && chown -R drift:drift /home/drift/.ssh && chmod 700 /home/drift/.ssh && chmod 600 /home/drift/.ssh/authorized_keys
```
Если заходите по паролю — добавьте ключ с ПК (PowerShell; ключ создаётся `ssh-keygen -t ed25519`):
```bash
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh drift@IP_СЕРВЕРА "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 700 ~/.ssh && chmod 600 ~/.ssh/authorized_keys"
```
Дальше работаем под `drift`:
```bash
ssh drift@IP_СЕРВЕРА
```

## Шаг 3. Ключ для GitHub (только чтение)
```bash
sudo apt update && sudo apt install -y git git-lfs
```
```bash
ssh-keygen -t ed25519 -f ~/.ssh/drift_deploy -N "" -C drift-vps && cat ~/.ssh/drift_deploy.pub
```
Выведенную строку: GitHub → репозиторий `drift` → **Settings → Deploy keys → Add deploy key** → вставить. Галочку **Allow write access не ставить**.

## Шаг 4. Скачать проект
```bash
git clone -b claude/practical-wright-antyod -c core.sshCommand="ssh -i ~/.ssh/drift_deploy -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new" git@github.com:Serega000777/drift.git ~/drift
```

## Шаг 5. Настроить сервер
```bash
sudo bash ~/drift/tools/setup/vps-setup.sh
```
Скрипт: пакеты (git, git-lfs, .NET 8, ufw, fail2ban), фаервол (открыты только SSH, 80, 443), автоматические обновления безопасности, защита SSH от перебора, таймер автообновления проекта и первый прогон тестов.

Когда убедитесь, что входите по ключу (новое окно, `ssh drift@IP_СЕРВЕРА`), отключите вход по паролю и под root:
```bash
sudo bash ~/drift/tools/setup/vps-setup.sh --harden-ssh
```

## Шаг 6. Проверить
```bash
cat ~/drift-status.log
```
Ожидается строка вида `… tests OK: Passed! - Failed: 0, Passed: 120 …`.

## Полезное
| Что | Команда |
|---|---|
| Обновить и прогнать тесты сейчас | `bash ~/drift/tools/setup/vps-update.sh --force` |
| Когда следующая проверка | `systemctl list-timers drift-update.timer` |
| Журнал обновлений | `journalctl -u drift-update.service -n 50` |
| Перейти на другую ветку (например, `main`) | `git -C ~/drift switch main` |
| Обновление «застряло» (`update blocked`) | `git -C ~/drift status` — на VPS не правьте файлы проекта руками |

## Безопасность
- Ключ VPS к GitHub — **только чтение**: если VPS взломают, код в GitHub не испортить.
- Таймер работает от обычного пользователя, не от root. Он запускает тесты из репозитория, поэтому **кто может пушить в ветку — может выполнить код на VPS** от этого пользователя: включите 2FA на GitHub, не давайте запись посторонним.
- Секретов в репозитории нет (скан при каждом коммите); будущие ключи сервера игры — только в переменных окружения/файлах на VPS с правами 600, не в git.
- Телеметрия .NET отключена (`DOTNET_CLI_TELEMETRY_OPTOUT=1`).

## На ПК после появления моделей
Перед `git pull` один раз выполнить `git lfs install`, иначе вместо моделей придут маленькие файлы-указатели.

## Дальше
Сервер игры (`docs/BACKEND_API.md`) разворачивается на этом же VPS: служба systemd, HTTPS (Caddy или nginx + Let's Encrypt, нужен домен), база PostgreSQL — после согласования пакетов. Если игроки из РФ, их персональные данные по 152-ФЗ должны храниться на серверах в РФ — уточнить у юриста.
