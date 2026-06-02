const state = {
  csrfToken: sessionStorage.getItem("centerales.csrfToken") || "",
  admin: null,
  jobs: [],
  apiKeys: [],
  users: [],
  audit: [],
  processor: null,
  health: null,
  selectedJobDetails: null,
  selectedSupportReport: null,
  activeTab: "overview"
};

const titles = {
  overview: ["Сводка", "Состояние очереди, обработчика и действий, которые требуют внимания."],
  delivery: ["Поставка", "Состав MVP-установки и локальный запуск без Docker."],
  jobs: ["Задачи", "Упавшие задачи и ручной retry."],
  processors: ["Обработчик", "Пассивное состояние PDF-обработчика, очередь, workers и diagnostics."],
  health: ["Проверки", "Локальные readiness checks без внешнего pdf2txt-вызова."],
  apiKeys: ["Ключи API", "Доступ клиента ЭП к публичному API."],
  users: ["Администраторы", "Учетные записи, пароли и отключение доступа."],
  audit: ["Аудит", "Журнал опасных admin-действий."]
};

document.addEventListener("DOMContentLoaded", () => {
  bindNavigation();
  bindForms();
  bindSessionActions();
  restoreSession();
});

window.addEventListener("unhandledrejection", event => {
  event.preventDefault();
  showAlert(event.reason?.message || "Действие не выполнено.", true);
});

function bindNavigation() {
  document.querySelectorAll("[data-tab]").forEach(button => {
    button.addEventListener("click", () => activateTab(button.dataset.tab));
  });

  document.querySelectorAll("[data-tab-jump]").forEach(button => {
    button.addEventListener("click", () => activateTab(button.dataset.tabJump));
  });
}

function bindForms() {
  document.getElementById("login-form").addEventListener("submit", async event => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    await login(form.get("login"), form.get("password"));
  });

  document.getElementById("create-user-form").addEventListener("submit", async event => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    await apiPost("/api/admin/users", {
      login: form.get("login"),
      password: form.get("password"),
      comment: form.get("comment") || null
    });
    event.currentTarget.reset();
    showAlert("Администратор создан.");
    await loadUsers();
    await loadAudit();
    renderAll();
  });

  document.getElementById("create-key-form").addEventListener("submit", async event => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const capabilities = String(form.get("capabilities"))
      .split(",")
      .map(value => value.trim())
      .filter(Boolean);
    const response = await apiPost("/api/admin/api-keys", {
      keyId: form.get("keyId"),
      name: form.get("name"),
      allowedCapabilities: capabilities,
      expiresAt: null,
      comment: form.get("comment") || null
    });
    event.currentTarget.reset();
    showCreatedSecret(response.keyId, response.secret);
    await loadApiKeys();
    await loadAudit();
    renderAll();
  });
}

function bindSessionActions() {
  document.getElementById("refresh-button").addEventListener("click", refreshData);
  document.getElementById("refresh-processor-button").addEventListener("click", async () => {
    await loadProcessor();
    renderProcessorDetails();
    renderOverview();
    showAlert("Состояние обработчика обновлено.");
  });
  document.getElementById("refresh-health-button").addEventListener("click", async () => {
    await loadHealth();
    renderHealthDetails();
    renderDeliveryDetails();
    showAlert("Проверки обновлены.");
  });
  document.getElementById("refresh-delivery-button").addEventListener("click", async () => {
    await Promise.all([loadHealth(), loadProcessor(), loadApiKeys()]);
    renderDeliveryDetails();
    renderOverview();
    showAlert("Поставка обновлена.");
  });
  document.getElementById("logout-button").addEventListener("click", logout);
  document.getElementById("retry-detail-button").addEventListener("click", () => {
    if (state.selectedJobDetails) {
      retryJob(state.selectedJobDetails);
    }
  });
  document.getElementById("support-report-button").addEventListener("click", downloadSupportReport);
  document.getElementById("close-job-details-button").addEventListener("click", clearJobDetails);
}

async function restoreSession() {
  try {
    const response = await apiGet("/api/admin/auth/me", false);
    state.admin = response.admin;
    setAuthenticated(true);
    await refreshData();
  } catch {
    setAuthenticated(false);
  }
}

async function login(loginValue, passwordValue) {
  try {
    const response = await fetchJson("/api/admin/auth/login", {
      method: "POST",
      body: JSON.stringify({ login: loginValue, password: passwordValue })
    });
    state.admin = response.admin;
    state.csrfToken = response.csrfToken;
    sessionStorage.setItem("centerales.csrfToken", state.csrfToken);
    setAuthenticated(true);
    showAlert("Вход выполнен.");
    await refreshData();
  } catch (error) {
    showAlert(error.message || "Не удалось войти.", true);
  }
}

async function logout() {
  try {
    await apiPost("/api/admin/auth/logout", {});
  } catch {
    // The local session should be cleared even if the server already expired it.
  }
  state.admin = null;
  state.csrfToken = "";
  sessionStorage.removeItem("centerales.csrfToken");
  setAuthenticated(false);
}

async function refreshData() {
  if (!state.admin) {
    return;
  }

  try {
    await Promise.all([
      loadJobs(),
      loadProcessor(),
      loadHealth(),
      loadApiKeys(),
      loadUsers(),
      loadAudit()
    ]);
    renderAll();
    showAlert("Данные обновлены.");
  } catch (error) {
    if (error.status === 401) {
      setAuthenticated(false);
      showAlert("Сессия истекла. Войдите снова.", true);
      return;
    }
    showAlert(error.message || "Не удалось обновить данные.", true);
  }
}

async function loadJobs() {
  const [failed, blocked] = await Promise.all([
    apiGet("/api/admin/jobs?status=failed&limit=50"),
    apiGet("/api/admin/jobs?status=blocked&limit=50")
  ]);
  state.jobs = [...(failed.jobs || []), ...(blocked.jobs || [])];
}

async function loadProcessor() {
  state.processor = await apiGet("/api/admin/processors/pdf2txt-http-recognizer");
}

async function loadHealth() {
  const [live, ready] = await Promise.all([
    fetchHealthJson("/health/live"),
    fetchHealthJson("/health/ready")
  ]);
  state.health = { live, ready };
}

async function loadApiKeys() {
  const response = await apiGet("/api/admin/api-keys?limit=100");
  state.apiKeys = response.keys || [];
}

async function loadUsers() {
  const response = await apiGet("/api/admin/users?limit=100");
  state.users = response.users || [];
}

async function loadAudit() {
  const response = await apiGet("/api/admin/audit?limit=50");
  state.audit = response.events || [];
}

function renderAll() {
  renderOverview();
  renderJobs();
  renderApiKeys();
  renderUsers();
  renderAudit();
  renderJobDetails();
  renderProcessorDetails();
  renderHealthDetails();
  renderDeliveryDetails();
}

function renderOverview() {
  const failedCount = state.jobs.length;
  const activeKeys = state.apiKeys.filter(key => key.isActive).length;
  const activeUsers = state.users.filter(user => user.isActive).length;

  setText("failed-count", failedCount);
  setText("failed-note", failedCount === 0 ? "Ручной retry не требуется" : "Откройте список задач");
  setText("processor-health", translateHealth(state.processor?.health));
  setText("processor-note", queueSummary(state.processor?.queue));
  setText("active-key-count", activeKeys);
  setText("active-user-count", activeUsers);

  const list = document.getElementById("overview-actions");
  list.innerHTML = "";
  if (failedCount === 0) {
    list.appendChild(workItem("Нет упавших задач", "Система не показывает задач, требующих ручного retry."));
  } else {
    state.jobs.slice(0, 5).forEach(job => {
      list.appendChild(workItem(
        `Задача ${shortId(job.jobId)} требует реакции`,
        `${translateStatus(job.status)}: ${job.normalizedError || "ошибка не указана"}`,
        () => retryJob(job)
      ));
    });
  }
}

function renderJobs() {
  const body = document.getElementById("jobs-body");
  body.innerHTML = "";
  if (state.jobs.length === 0) {
    appendEmptyRow(body, 6, "Нет failed или blocked задач.");
    return;
  }

  state.jobs.forEach(job => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${statusPill(job.status)}</td>
      <td><span class="mono">${escapeHtml(job.jobId)}</span><br>${escapeHtml(job.hash || "")}</td>
      <td>${job.attemptNumber}</td>
      <td>${escapeHtml(job.normalizedError || "Не указана")}</td>
      <td>${escapeHtml(job.endpoint || "Не выбран")}</td>
      <td></td>
    `;
    const openButton = document.createElement("button");
    openButton.className = "secondary-button";
    openButton.type = "button";
    openButton.textContent = "Детали";
    openButton.addEventListener("click", () => loadJobDetails(job.jobId));
    row.lastElementChild.appendChild(openButton);

    const button = document.createElement("button");
    button.className = "primary-button";
    button.type = "button";
    button.textContent = "Retry";
    button.style.marginLeft = "8px";
    button.addEventListener("click", () => retryJob(job));
    row.lastElementChild.appendChild(button);
    body.appendChild(row);
  });
}

function renderProcessorDetails() {
  const processor = state.processor;
  const queue = processor?.queue || {};

  setText("processor-details-health", translateHealth(processor?.health));
  setText("processor-details-key", processor?.processorKey || "pdf2txt-http-recognizer");
  setText("processor-details-queued", queue.queued ?? 0);
  setText("processor-details-processing", queue.processing ?? 0);
  setText("processor-details-problem-count", (queue.failed ?? 0) + (queue.blocked ?? 0));

  renderDefinitionList("processor-queue-list", [
    ["Queued", queue.queued ?? 0],
    ["Processing", queue.processing ?? 0],
    ["Completed", queue.completed ?? 0],
    ["Failed", queue.failed ?? 0],
    ["Blocked", queue.blocked ?? 0],
    ["Cancelled", queue.cancelled ?? 0]
  ]);

  renderProcessorWorkers(processor?.workers || []);
  renderProcessorDiagnostics(processor?.recentDiagnostics || []);
}

function renderProcessorWorkers(workers) {
  const body = document.getElementById("processor-workers-body");
  body.innerHTML = "";
  if (workers.length === 0) {
    appendEmptyRow(body, 4, "Активные worker heartbeat не найдены.");
    return;
  }

  workers.forEach(worker => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td><span class="mono">${escapeHtml(worker.workerId)}</span></td>
      <td>${formatDate(worker.startedAt)}</td>
      <td>${formatDate(worker.heartbeatAt)}</td>
      <td>${worker.stale ? statusPill("stale") : statusPill("active")}</td>
    `;
    body.appendChild(row);
  });
}

function renderProcessorDiagnostics(diagnostics) {
  const body = document.getElementById("processor-diagnostics-body");
  body.innerHTML = "";
  if (diagnostics.length === 0) {
    appendEmptyRow(body, 7, "Recent diagnostics пусты.");
    return;
  }

  diagnostics.forEach(diagnostic => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td><span class="mono">${escapeHtml(diagnostic.jobId)}</span></td>
      <td>${diagnostic.attemptNumber}</td>
      <td>${statusPill(diagnostic.status)}</td>
      <td>${escapeHtml(diagnostic.endpoint || "Не выбран")}</td>
      <td>${diagnostic.httpStatus ?? "Нет данных"}</td>
      <td>${escapeHtml(diagnostic.normalizedError || "Нет данных")}</td>
      <td><span class="mono">${escapeHtml(diagnostic.correlationId || "Нет данных")}</span></td>
    `;
    body.appendChild(row);
  });
}

function renderHealthDetails() {
  const live = state.health?.live;
  const ready = state.health?.ready;
  const checks = ready?.checks || [];
  const problemCount = checks.filter(check => check.status !== "healthy").length;

  setText("health-live-status", translateHealth(live?.status));
  setText("health-live-checked", live?.checkedAt ? formatDate(live.checkedAt) : "Проверка не запускалась");
  setText("health-ready-status", translateHealth(ready?.status));
  setText("health-ready-checked", ready?.checkedAt ? formatDate(ready.checkedAt) : "Проверка не запускалась");
  setText("health-check-count", checks.length);
  setText("health-problem-count", problemCount);

  const body = document.getElementById("health-checks-body");
  body.innerHTML = "";
  if (checks.length === 0) {
    appendEmptyRow(body, 3, "Ready checks еще не загружены.");
    return;
  }

  checks.forEach(check => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${escapeHtml(translateHealthCheckName(check.name))}</td>
      <td>${statusPill(check.status)}</td>
      <td>${escapeHtml(describeHealthCheck(check.name, check.status))}</td>
    `;
    body.appendChild(row);
  });
}

function renderDeliveryDetails() {
  const readyChecks = state.health?.ready?.checks || [];
  const checkStatus = new Map(readyChecks.map(check => [check.name, check.status]));
  const activeKeys = state.apiKeys.filter(key => key.isActive).length;
  const workerCount = state.processor?.workers?.length ?? 0;

  renderDefinitionList("delivery-runtime-list", [
    ["Admin UI", "/admin"],
    ["Web", "dotnet run --project src\\Apps\\CenteralES.Web\\CenteralES.Web.csproj"],
    ["Worker", "dotnet run --project src\\Apps\\CenteralES.Worker\\CenteralES.Worker.csproj"],
    ["Database config", "db.env"],
    ["Admin credentials", "logon.env"]
  ]);

  const components = [
    {
      name: "CenteralES.Web",
      role: "Public API, Admin API, Admin UI, health endpoints",
      status: state.health?.live?.status || "unknown",
      comment: "Отдается локально через ASP.NET Core."
    },
    {
      name: "CenteralES.Worker",
      role: "Фоновая обработка очереди PDF",
      status: workerCount > 0 ? "healthy" : "unknown",
      comment: workerCount > 0 ? `worker heartbeat: ${workerCount}` : "Heartbeat появится после запуска Worker."
    },
    {
      name: "PostgreSQL",
      role: "Очередь, результаты, admin/auth/audit",
      status: checkStatus.get("postgres") || "unknown",
      comment: "Проверяется через /health/ready."
    },
    {
      name: "Processing schema",
      role: "Совместимость таблиц MVP",
      status: checkStatus.get("processingSchema") || "unknown",
      comment: "Web применяет схему при старте."
    },
    {
      name: "Temporary storage",
      role: "Временное хранение входных PDF до terminal state",
      status: checkStatus.get("temporaryStorage") || "unknown",
      comment: "Проверяется запись, чтение и capacity guard."
    },
    {
      name: "pdf2txt-http-recognizer",
      role: "Внешний обработчик PDF stamp recognition",
      status: state.processor?.health || "unknown",
      comment: "В админке показывается passive status без вызова /recognize_json/."
    },
    {
      name: "API keys",
      role: "Доступ клиента ЭП к Public API",
      status: activeKeys > 0 ? "healthy" : "unknown",
      comment: activeKeys > 0 ? `активных ключей: ${activeKeys}` : "Создаются на вкладке Ключи API."
    }
  ];

  const body = document.getElementById("delivery-components-body");
  body.innerHTML = "";
  components.forEach(component => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${escapeHtml(component.name)}</td>
      <td>${escapeHtml(component.role)}</td>
      <td>${statusPill(component.status)}</td>
      <td>${escapeHtml(component.comment)}</td>
    `;
    body.appendChild(row);
  });
}

function translateHealthCheckName(name) {
  const map = {
    postgres: "PostgreSQL",
    processingSchema: "Схема БД",
    temporaryStorage: "Временное хранилище"
  };
  return map[name] || name || "Неизвестная проверка";
}

function describeHealthCheck(name, status) {
  if (status !== "healthy") {
    const unhealthy = {
      postgres: "Web не может использовать PostgreSQL.",
      processingSchema: "Схема БД неполная или несовместима.",
      temporaryStorage: "Временное хранилище недоступно или заполнено."
    };
    return unhealthy[name] || "Проверка завершилась проблемой.";
  }

  const healthy = {
    postgres: "Соединение с PostgreSQL работает.",
    processingSchema: "Обязательные таблицы найдены.",
    temporaryStorage: "Запись, чтение и capacity guard работают."
  };
  return healthy[name] || "Проверка прошла успешно.";
}

async function loadJobDetails(jobId) {
  try {
    const encodedJobId = encodeURIComponent(jobId);
    const [details, supportReport] = await Promise.all([
      apiGet(`/api/admin/jobs/${encodedJobId}`),
      apiGet(`/api/admin/jobs/${encodedJobId}/support-report`)
    ]);
    state.selectedJobDetails = details;
    state.selectedSupportReport = supportReport;
    renderJobDetails();
    showAlert("Детали задачи загружены.");
  } catch (error) {
    showAlert(error.message || "Не удалось загрузить детали задачи.", true);
  }
}

function renderJobDetails() {
  const panel = document.getElementById("job-details-panel");
  const job = state.selectedJobDetails;
  const report = state.selectedSupportReport;
  panel.hidden = !job;
  if (!job) {
    return;
  }

  setText("job-details-title", `Задача ${shortId(job.jobId)}: ${translateStatus(job.status)}`);
  setText("job-details-subtitle", `${job.capability} · попытка ${job.attemptNumber} · hash ${job.hash}`);

  document.getElementById("job-details-meta").innerHTML = [
    detailBlock("Идентификаторы", [
      ["Job ID", job.jobId],
      ["Subject ID", job.subjectId],
      ["Hash", job.hash],
      ["Temporary file", job.temporaryFileKey]
    ]),
    detailBlock("Время", [
      ["Создана", formatDate(job.createdAt)],
      ["Запланирована", formatDate(job.scheduledAt)],
      ["Старт", formatDate(job.startedAt)],
      ["Финиш", formatDate(job.finishedAt)],
      ["Heartbeat", formatDate(job.heartbeatAt)]
    ])
  ].join("");

  renderDefinitionList("job-details-diagnostics", [
    ["Endpoint", job.diagnostics?.endpoint || "Не выбран"],
    ["Duration", formatDuration(job.diagnostics?.durationMs)],
    ["HTTP", job.diagnostics?.httpStatus ?? "Нет данных"],
    ["Normalized error", job.diagnostics?.normalizedError || "Нет данных"],
    ["Retryable", formatBool(job.diagnostics?.retryable)],
    ["Correlation ID", job.diagnostics?.correlationId || "Нет данных"],
    ["Excerpt", job.diagnostics?.rawErrorExcerpt || "Нет данных"]
  ]);

  renderSupportReportSummary(report);
  renderAttempts(job.attempts || []);

  const retryButton = document.getElementById("retry-detail-button");
  retryButton.disabled = job.status !== "failed" && job.status !== "blocked";
}

function renderSupportReportSummary(report) {
  const target = document.getElementById("support-report-summary");
  if (!report) {
    target.textContent = "Отчет недоступен.";
    return;
  }

  target.innerHTML = `
    <div>Сформирован: ${escapeHtml(formatDate(report.generatedAt))}</div>
    <div>Processor: <code>${escapeHtml(report.processorKey)}</code></div>
    <div>Health: ${escapeHtml(translateHealth(report.processor?.health))}</div>
    <div>Audit events: ${report.auditEvents?.length ?? 0}</div>
    <div>Result: ${report.result ? escapeHtml(report.result.resultKind) : "Нет результата"}</div>
  `;
}

function renderAttempts(attempts) {
  const body = document.getElementById("job-attempts-body");
  body.innerHTML = "";
  if (attempts.length === 0) {
    appendEmptyRow(body, 6, "История попыток недоступна.");
    return;
  }

  attempts.forEach(attempt => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${attempt.attemptNumber}</td>
      <td>${statusPill(attempt.status)}</td>
      <td>${escapeHtml(attempt.endpoint || "Не выбран")}</td>
      <td>${attempt.httpStatus ?? "Нет данных"}</td>
      <td>${escapeHtml(attempt.normalizedError || "Нет данных")}</td>
      <td><span class="mono">${escapeHtml(attempt.correlationId || "Нет данных")}</span></td>
    `;
    body.appendChild(row);
  });
}

function clearJobDetails() {
  state.selectedJobDetails = null;
  state.selectedSupportReport = null;
  renderJobDetails();
}

function downloadSupportReport() {
  if (!state.selectedSupportReport) {
    showAlert("Сначала откройте детали задачи.", true);
    return;
  }

  const jobId = state.selectedSupportReport.jobId || state.selectedJobDetails?.jobId || "job";
  const blob = new Blob(
    [JSON.stringify(state.selectedSupportReport, null, 2)],
    { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob);
  link.download = `centerales-support-report-${shortId(jobId)}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(link.href);
}

function renderApiKeys() {
  const body = document.getElementById("api-keys-body");
  body.innerHTML = "";
  if (state.apiKeys.length === 0) {
    appendEmptyRow(body, 5, "Ключи API не найдены.");
    return;
  }

  state.apiKeys.forEach(key => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td><span class="mono">${escapeHtml(key.keyId)}</span></td>
      <td>${escapeHtml(key.name)}</td>
      <td>${escapeHtml((key.allowedCapabilities || []).join(", "))}</td>
      <td>${key.isActive ? statusPill("active") : statusPill("disabled")}</td>
      <td></td>
    `;
    if (key.isActive) {
      const button = document.createElement("button");
      button.className = "danger-button";
      button.type = "button";
      button.textContent = "Отключить";
      button.addEventListener("click", () => disableApiKey(key));
      row.lastElementChild.appendChild(button);
    }
    body.appendChild(row);
  });
}

function renderUsers() {
  const body = document.getElementById("users-body");
  body.innerHTML = "";
  if (state.users.length === 0) {
    appendEmptyRow(body, 5, "Администраторы не найдены.");
    return;
  }

  state.users.forEach(user => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td>${escapeHtml(user.login)}</td>
      <td>${escapeHtml(user.role)}</td>
      <td>${user.isActive ? statusPill("active") : statusPill("disabled")}</td>
      <td>${formatDate(user.lastLoginAt)}</td>
      <td></td>
    `;
    const actions = row.lastElementChild;
    const passwordButton = document.createElement("button");
    passwordButton.className = "secondary-button";
    passwordButton.type = "button";
    passwordButton.textContent = "Пароль";
    passwordButton.disabled = !user.isActive;
    passwordButton.addEventListener("click", () => changeUserPassword(user));
    actions.appendChild(passwordButton);

    if (user.isActive) {
      const disableButton = document.createElement("button");
      disableButton.className = "danger-button";
      disableButton.type = "button";
      disableButton.textContent = "Отключить";
      disableButton.style.marginLeft = "8px";
      disableButton.addEventListener("click", () => disableUser(user));
      actions.appendChild(disableButton);
    }
    body.appendChild(row);
  });
}

function renderAudit() {
  const list = document.getElementById("audit-list");
  list.innerHTML = "";
  if (state.audit.length === 0) {
    list.appendChild(workItem("Записей аудита нет", "Опасные действия пока не выполнялись."));
    return;
  }

  state.audit.forEach(event => {
    const item = document.createElement("div");
    item.className = "audit-item";
    item.innerHTML = `
      <strong>${escapeHtml(translateAction(event.action))}</strong>
      <div>${escapeHtml(event.actorLogin || "system")} -> ${escapeHtml(event.targetType)} / ${escapeHtml(event.targetId)}</div>
      <div>${formatDate(event.occurredAt)} · correlationId <span class="mono">${escapeHtml(event.correlationId)}</span></div>
      ${event.comment ? `<div>Комментарий: ${escapeHtml(event.comment)}</div>` : ""}
    `;
    list.appendChild(item);
  });
}

async function retryJob(job) {
  const jobId = job.jobId;
  const comment = await confirmAction(
    "Запустить retry",
    `Будет создана новая попытка для задачи ${shortId(jobId)}. Действие попадет в аудит.`,
    false
  );
  if (comment === null) {
    return;
  }
  await apiPost(`/api/admin/jobs/${encodeURIComponent(jobId)}/retry`, { comment: comment || null });
  showAlert("Retry запущен.");
  await loadJobs();
  if (state.selectedJobDetails?.jobId === jobId) {
    await loadJobDetails(jobId);
  }
  await loadAudit();
  renderAll();
}

async function disableApiKey(key) {
  const comment = await confirmAction(
    "Отключить ключ API",
    `Клиент с ключом ${key.keyId} больше не сможет обращаться к Public API.`,
    true
  );
  if (comment === null) {
    return;
  }
  await apiPost(`/api/admin/api-keys/${encodeURIComponent(key.keyId)}/disable`, { comment });
  showAlert("Ключ API отключен.");
  await loadApiKeys();
  await loadAudit();
  renderAll();
}

async function disableUser(user) {
  const comment = await confirmAction(
    "Отключить администратора",
    `Администратор ${user.login} потеряет доступ к админке. Активные сессии будут отозваны.`,
    true
  );
  if (comment === null) {
    return;
  }
  await apiPost(`/api/admin/users/${encodeURIComponent(user.userId)}/disable`, { comment });
  showAlert("Администратор отключен.");
  await loadUsers();
  await loadAudit();
  renderAll();
}

async function changeUserPassword(user) {
  const password = window.prompt(`Новый пароль для ${user.login}. Минимум 8 символов.`);
  if (password === null) {
    return;
  }
  if (password.length < 8) {
    showAlert("Пароль должен быть не короче 8 символов.", true);
    return;
  }
  const comment = await confirmAction(
    "Изменить пароль",
    `Пароль администратора ${user.login} будет изменен. Сессии пользователя будут отозваны.`,
    false
  );
  if (comment === null) {
    return;
  }
  await apiPost(`/api/admin/users/${encodeURIComponent(user.userId)}/password`, {
    password,
    comment: comment || null
  });
  showAlert("Пароль изменен.");
  await loadUsers();
  await loadAudit();
  renderAll();
}

function confirmAction(title, message, requireComment) {
  const dialog = document.getElementById("confirm-dialog");
  const titleEl = document.getElementById("confirm-title");
  const messageEl = document.getElementById("confirm-message");
  const commentEl = document.getElementById("confirm-comment");
  const labelEl = document.getElementById("confirm-comment-label");
  const okButton = document.getElementById("confirm-ok");

  titleEl.textContent = title;
  messageEl.textContent = message;
  commentEl.value = "";
  commentEl.required = requireComment;
  labelEl.firstChild.textContent = requireComment ? "Комментарий обязателен" : "Комментарий";
  okButton.textContent = title;

  return new Promise(resolve => {
    const closeHandler = () => {
      dialog.removeEventListener("close", closeHandler);
      if (dialog.returnValue !== "ok") {
        resolve(null);
        return;
      }
      const comment = commentEl.value.trim();
      if (requireComment && !comment) {
        showAlert("Для этого действия нужен комментарий.", true);
        resolve(null);
        return;
      }
      resolve(comment);
    };
    dialog.addEventListener("close", closeHandler);
    dialog.showModal();
  });
}

function activateTab(tab) {
  state.activeTab = tab;
  document.querySelectorAll(".nav-item").forEach(button => {
    button.classList.toggle("is-active", button.dataset.tab === tab);
  });
  document.querySelectorAll(".tab-panel").forEach(panel => {
    panel.hidden = panel.id !== `${tab}-tab`;
  });
  const [title, subtitle] = titles[tab] || titles.overview;
  setText("page-title", title);
  setText("page-subtitle", subtitle);
}

function setAuthenticated(isAuthenticated) {
  document.getElementById("login-panel").hidden = isAuthenticated;
  document.getElementById("admin-content").hidden = !isAuthenticated;
  document.getElementById("logout-button").hidden = !isAuthenticated;
  setText("session-login", isAuthenticated ? state.admin.login : "Не выполнен вход");
}

async function apiGet(url, requireAuth = true) {
  return fetchJson(url, { method: "GET" }, requireAuth);
}

async function apiPost(url, body) {
  return fetchJson(url, {
    method: "POST",
    headers: { "X-CSRF-Token": state.csrfToken },
    body: JSON.stringify(body)
  });
}

async function fetchJson(url, options = {}, requireAuth = true) {
  const response = await fetch(url, {
    credentials: "same-origin",
    headers: {
      "Accept": "application/json",
      "Content-Type": "application/json",
      ...(options.headers || {})
    },
    ...options
  });

  if (!response.ok) {
    const payload = await tryReadJson(response);
    const error = new Error(payload?.error?.message || `HTTP ${response.status}`);
    error.status = response.status;
    if (requireAuth && response.status === 401) {
      state.admin = null;
    }
    throw error;
  }

  return tryReadJson(response);
}

async function fetchHealthJson(url) {
  const response = await fetch(url, {
    credentials: "same-origin",
    headers: { "Accept": "application/json" }
  });
  const payload = await tryReadJson(response);
  if (!response.ok && response.status !== 503) {
    const error = new Error(payload?.error?.message || `HTTP ${response.status}`);
    error.status = response.status;
    throw error;
  }
  return payload;
}

async function tryReadJson(response) {
  const text = await response.text();
  return text ? JSON.parse(text) : null;
}

function showAlert(message, isError = false) {
  const alert = document.getElementById("alert");
  alert.textContent = message;
  alert.classList.toggle("is-error", isError);
  alert.hidden = false;
}

function showCreatedSecret(keyId, secret) {
  const box = document.getElementById("created-secret");
  box.hidden = false;
  box.innerHTML = `
    <strong>Секрет для ${escapeHtml(keyId)} показан один раз.</strong>
    <div class="mono">${escapeHtml(secret)}</div>
  `;
}

function workItem(title, text, action) {
  const item = document.createElement("div");
  item.className = "work-item";
  item.innerHTML = `<strong>${escapeHtml(title)}</strong><div>${escapeHtml(text)}</div>`;
  if (action) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "primary-button";
    button.textContent = "Открыть действие";
    button.style.marginTop = "10px";
    button.addEventListener("click", action);
    item.appendChild(button);
  }
  return item;
}

function appendEmptyRow(body, columns, text) {
  const row = document.createElement("tr");
  const cell = document.createElement("td");
  cell.colSpan = columns;
  cell.textContent = text;
  row.appendChild(cell);
  body.appendChild(row);
}

function detailBlock(title, rows) {
  return `
    <div class="detail-block">
      <h3>${escapeHtml(title)}</h3>
      <dl class="kv-list">
        ${rows.map(([name, value]) => `
          <dt>${escapeHtml(name)}</dt>
          <dd>${escapeHtml(value ?? "Нет данных")}</dd>
        `).join("")}
      </dl>
    </div>
  `;
}

function renderDefinitionList(id, rows) {
  document.getElementById(id).innerHTML = rows.map(([name, value]) => `
    <dt>${escapeHtml(name)}</dt>
    <dd>${escapeHtml(value ?? "Нет данных")}</dd>
  `).join("");
}

function statusPill(status) {
  const kind = status === "active" || status === "completed" ? "ok"
    : status === "disabled" || status === "failed" || status === "blocked" ? "danger"
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
    unhealthy: "Проблема",
    unknown: "Нет данных",
    failed: "Ошибка",
    blocked: "Заблокирована",
    stale: "Нет heartbeat",
    completed: "Готово",
    processing: "В работе",
    queued: "В очереди",
    cancelled: "Отменена"
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

function setText(id, value) {
  document.getElementById(id).textContent = value;
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}
