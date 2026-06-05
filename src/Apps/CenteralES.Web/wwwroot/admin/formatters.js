(function () {
  function statusPill(status) {
    const kind = status === "active" || status === "completed" || status === "healthy" || status === "succeeded" ? "ok"
      : status === "disabled" || status === "failed" || status === "blocked" || status === "full" ? "danger"
        : "warn";
    return `<span class="status-pill ${kind}">${escapeHtml(translateStatus(status))}</span>`;
  }

  function queueSummary(queue) {
    if (!queue) {
      return "Очередь недоступна";
    }
    return `queued ${queue.queued}, processing ${queue.processing}, failed ${queue.failed}, blocked ${queue.blocked}`;
  }

  function translateStatus(status) {
    const map = {
      active: "Активен",
      disabled: "Отключен",
      healthy: "Работает",
      warning: "Предупреждение",
      full: "Заполнено",
      unhealthy: "Проблема",
      unknown: "Нет данных",
      failed: "Ошибка",
      blocked: "Заблокирована",
      stale: "Нет heartbeat",
      completed: "Готово",
      processing: "В работе",
      queued: "В очереди",
      cancelled: "Отменена",
      succeeded: "Успешно",
      notConfigured: "Не настроено"
    };
    return map[status] || status || "Нет данных";
  }

  function translateHealth(health) {
    const map = {
      healthy: "Работает",
      degraded: "Есть проблемы",
      unhealthy: "Недоступен",
      unknown: "Нет данных"
    };
    return map[health] || health || "Нет данных";
  }

  function translateAction(action) {
    const map = {
      manual_retry_job: "Ручной retry задачи",
      create_api_key: "Создание ключа API",
      disable_api_key: "Отключение ключа API",
      create_admin_user: "Создание администратора",
      bootstrap_admin_user: "Первичный bootstrap администратора",
      disable_admin_user: "Отключение администратора",
      change_admin_password: "Смена пароля администратора"
    };
    return map[action] || action;
  }

  function shortId(value) {
    return value ? value.slice(0, 8) : "";
  }

  function formatDate(value) {
    if (!value) {
      return "Нет данных";
    }
    return new Intl.DateTimeFormat("ru-RU", {
      dateStyle: "short",
      timeStyle: "short"
    }).format(new Date(value));
  }

  function formatDuration(value) {
    return typeof value === "number" ? `${Math.round(value)} ms` : "Нет данных";
  }

  function formatBool(value) {
    return value === true ? "Да" : value === false ? "Нет" : "Нет данных";
  }

  function formatBytes(value) {
    if (typeof value !== "number") {
      return "Не задано";
    }
    const units = ["B", "KiB", "MiB", "GiB", "TiB"];
    let size = value;
    let unitIndex = 0;
    while (size >= 1024 && unitIndex < units.length - 1) {
      size /= 1024;
      unitIndex += 1;
    }
    const precision = unitIndex === 0 ? 0 : 1;
    return `${size.toFixed(precision)} ${units[unitIndex]}`;
  }

  function formatList(values) {
    if (!Array.isArray(values) || values.length === 0) {
      return "Нет данных";
    }

    return values.join(", ");
  }

  function pluralizeRu(count, one, few, many) {
    const mod10 = count % 10;
    const mod100 = count % 100;
    if (mod10 === 1 && mod100 !== 11) {
      return one;
    }
    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
      return few;
    }
    return many;
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  window.CenteralESAdminFormatters = {
    statusPill,
    queueSummary,
    translateStatus,
    translateHealth,
    translateAction,
    shortId,
    formatDate,
    formatDuration,
    formatBool,
    formatBytes,
    formatList,
    pluralizeRu,
    escapeHtml
  };
})();
