# Контракт сервера аккаунтов (черновик v1)

Клиентская сторона уже реализована: `IAuthBackend`, `AuthService`, `AccountState.Merge`, `PrivacyData`. Этот документ — договор для серверной реализации (VPS или облако).

## Общие правила
- Только HTTPS (TLS 1.2+), JSON UTF-8, базовый путь `/v1`.
- Авторизация: `Authorization: Bearer <access_token>` (JWT, подпись сервера EdDSA/RS256, срок 15 минут; `sub` = accountId, `plt` = платформа, `jti`).
- Refresh-токен: непрозрачный, одноразовый (ротация), срок 30–60 дней; повторное использование старого → отзыв всей «семьи» токенов (`refresh_reused`).
- Ограничение частоты (rate limit) на вход/обновление по IP и устройству; ответ `429` с `Retry-After`.
- Ошибки: `{ "error": "<code>", "retryable": true|false }`. Коды: `invalid_proof`, `revoked`, `refresh_reused`, `rate_limited`, `maintenance`, `age_restricted`.
- Никогда не логировать токены и пропуски платформ.

## Вход
`POST /v1/auth/{provider}` — provider: `steam | google | gamecenter | apple | psn | xbox | nintendo | epic`
```json
{ "proof": "<одноразовый пропуск платформы>", "platformUserId": "<id>", "installId": "<random hex>", "client": { "version": "0.1.0", "platform": "Android" } }
```
Сервер проверяет пропуск у платформы:
- Steam: Web API `ISteamUserAuth/AuthenticateUserTicket` (ключ издателя только на сервере).
- Google Play Games: обмен server auth code на токены Google.
- Game Center: проверка подписи identity verification (сертификат Apple, соль, таймстамп).
- Sign in with Apple: проверка identity token (JWT) по JWKS Apple, `aud` = bundle id.
- PSN / Xbox / Nintendo / Epic: по их серверным API (после подписания соглашений).
Ответ `200`:
```json
{ "accountId": "acc_…", "accessToken": "…", "accessExpiresUtc": "2026-11-05T12:15:00Z", "refreshToken": "…", "displayName": "Drifter-4F2A", "isNew": false }
```

## Обновление и выход
- `POST /v1/auth/refresh` `{ "refreshToken": "…" }` → новая пара токенов (старый refresh становится недействительным).
- `POST /v1/auth/signout` `{ "refreshToken": "…" }` → `204`.
- `POST /v1/auth/signout-all` (Bearer) → отзыв всех сессий аккаунта.
- `POST /v1/auth/link/{provider}` (Bearer) `{ "proof": "…" }` → привязка ещё одной платформы к аккаунту (кросс-прогрессия).

## Профиль и прогресс
- `GET /v1/profile` → `AccountState` (+ `revision`).
- `PUT /v1/profile` с `If-Match: <revision>` → оптимистичная блокировка; при конфликте `409` + серверная версия, клиент делает `AccountState.Merge`.
- Кошелёк меняется **только** серверными операциями: `POST /v1/wallet/earn` принимает подтверждения (запись заезда), а не число кредитов.

## Заезды, рекорды, тандем
- `POST /v1/runs` — запись `GhostRecording` + итог; сервер прогоняет проверки правдоподобия (то же ядро `DriftCareer.Core` без Unity) и только потом засчитывает рекорд/награду.
- `GET /v1/leaderboards/{mode}` — ник показывается только если `Capabilities.publicNameOnLeaderboards`.
- Онлайн-тандем: лобби `POST /v1/lobbies`, далее realtime-транспорт (`ITandemTransport`).

## Конфиденциальность
- `GET /v1/account/export` → архив всех серверных данных аккаунта.
- `DELETE /v1/account` → удаление аккаунта и данных в течение 30 дней (кроме требуемого законом, напр. учёта покупок).
- `PUT /v1/privacy` → согласия и возрастная группа (сервер применяет те же правила, что `PrivacySettings.Enforce`).
