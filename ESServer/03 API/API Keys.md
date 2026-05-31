# API Keys

## Назначение

API key используется для доступа desktop-приложения сервиса ЭП к Public API.

Выбран вариант API key A+:

```http
Authorization: ApiKey <secret>
```

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

## Модель

```text
ClientApplication
  id
  name
  keyHash
  isActive
  allowedCapabilities
  createdAt
  expiresAt nullable
  lastUsedAt nullable
```

## Проверка доступа

Поток:

```text
1. Запрос приходит в Public API.
2. Middleware читает Authorization header.
3. Проверяется формат ApiKey.
4. Считается hash ключа.
5. Ищется активный ClientApplication.
6. Проверяется allowedCapabilities.
7. Обновляется lastUsedAt.
8. Use case выполняется.
```

## Ошибки

```http
401 Unauthorized
```

Если ключ отсутствует, неверный или отключён.

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

## Будущее расширение

Позже можно добавить:

- HMAC-подпись запроса;
- срок действия ключей;
- привязку к организации;
- привязку к рабочему месту;
- JWT/OAuth flow;
- IP allowlist;
- quota/rate limits.

Модель `ClientApplication` не должна мешать этим расширениям.
