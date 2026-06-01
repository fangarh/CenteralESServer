# API Keys

## Назначение

API key используется для доступа desktop-приложения сервиса ЭП к Public API.

Выбран вариант API key A+:

```http
Authorization: ApiKey <keyId>.<secret>
```

`keyId` — публичный идентификатор ключа. `secret` показывается только один раз при создании или ротации.

## Почему не OAuth/JWT в MVP

Для первого релиза не хочется усложнять взаимодействие клиента ЭП и серверной разработки.

OAuth/JWT-flow добавит:

- получение token;
- expiry;
- refresh;
- дополнительные ошибки;
- дополнительный lifecycle;
- усложнение desktop-клиента.

Для контролируемого desktop-клиента и серверного компонента поставки достаточно API key при условии нормальной реализации.

## Что значит A+

Это не “просто строка в конфиге”.

Ключи должны:

- храниться в БД только в виде hash;
- иметь имя;
- иметь статус активности;
- иметь список разрешённых capability;
- иметь lastUsedAt;
- иметь audit;
- уметь отключаться;
- уметь перевыпускаться.

На стороне desktop-приложения ЭП secret хранится в Windows Credential Manager. В конфиге приложения можно хранить только `serverUrl` и `keyId`.

## Модель

```text
ClientApplication
  id
  keyId
  name
  secretHash
  isActive
  allowedCapabilities
  createdAt
  expiresAt nullable
  lastUsedAt nullable
  lastUsedIp nullable
  lastUsedUserAgent nullable
  disabledAt nullable
```

Для MVP `expiresAt` поддерживается моделью, но по умолчанию равен `null`, то есть ключ бессрочный.

## Проверка доступа

Поток:

```text
1. Запрос приходит в Public API.
2. Middleware читает Authorization header.
3. Проверяется формат `ApiKey <keyId>.<secret>`.
4. По `keyId` ищется активный ClientApplication.
5. Проверяется hash secret-а.
6. Проверяется allowedCapabilities.
7. Обновляются lastUsedAt, lastUsedIp и lastUsedUserAgent.
8. Use case выполняется.
```

Текущий backend skeleton уже реализует этот baseline для Public API:

- таблица `client_applications`;
- `secretHash` в формате PBKDF2-SHA256, raw secret в БД не хранится;
- проверка `isActive`, `expiresAt` и `allowedCapabilities`;
- обновление `lastUsedAt`, `lastUsedIp`, `lastUsedUserAgent` после успешной проверки;
- `POST /api/pdf-stamp-recognition/jobs`, `GET /api/pdf-stamp-recognition/results/{hash}` и `GET /api/jobs/{jobId}` требуют `Authorization: ApiKey <keyId>.<secret>`.

Создание, отключение и rotation ключей через Admin UI/Admin API остаются отдельным admin-action блоком. Для integration/smoke тестов ключ сидится напрямую в PostgreSQL.

## Ошибки

```http
401 Unauthorized
```

Если ключ отсутствует, формат неверный, `keyId` не найден, secret не совпал, ключ отключён или истёк.

```http
403 Forbidden
```

Если ключ валиден, но не имеет права на capability.

## Управление в админке

Администратор должен уметь:

- создать ключ;
- увидеть имя ключа;
- увидеть capabilities;
- увидеть last used;
- отключить ключ;
- перевыпустить ключ;
- посмотреть audit событий ключа.

Полный secret показывается только один раз при создании или ротации.

Ручная ротация MVP:

1. создать новый key;
2. прописать его в desktop-приложении;
3. проверить работу;
4. отключить старый key;
5. проверить audit и lastUsed старого key.

Если ключ скомпрометирован, администратор отключает key, создаёт новый и проверяет audit/lastUsed.

## Будущее расширение

Позже можно добавить:

- HMAC-подпись запроса;
- обязательный срок действия ключей;
- привязку к организации;
- привязку к рабочему месту;
- привязку к инсталляции;
- JWT/OAuth flow;
- IP allowlist;
- quota/rate limits.

Модель `ClientApplication` не должна мешать этим расширениям.
