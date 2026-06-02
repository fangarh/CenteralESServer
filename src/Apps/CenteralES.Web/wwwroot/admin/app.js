const {
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
} = window.CenteralESAdminFormatters;

const {
  showAlert,
  showCreatedSecret,
  workItem,
  appendEmptyRow,
  detailBlock,
  renderDefinitionList,
  setText
} = window.CenteralESAdminDom;

const state = {
  csrfToken: sessionStorage.getItem("centerales.csrfToken") || "",
  admin: null,
  jobs: [],
  results: [],
  apiKeys: [],
  users: [],
  audit: [],
  auditFilters: {
    action: "",
    targetType: "",
    targetId: "",
    actor: "",
    occurredFrom: "",
    occurredTo: "",
    limit: "50"
  },
  processor: null,
  health: null,
  storage: null,
  settings: null,
  selectedJobDetails: null,
  selectedSupportReport: null,
  selectedResult: null,
  activeTab: "overview"
};

const {
  apiGet,
  apiPost,
  fetchJson,
  fetchHealthJson
} = window.CenteralESAdminHttp.createAdminApiClient({
  getCsrfToken: () => state.csrfToken,
  onUnauthorized: () => {
    state.admin = null;
  }
});

const confirmAction = window.CenteralESAdminDialog.createConfirmAction({ showAlert });

const titles = {
  overview: ["Сводка", "Состояние очереди, обработчика и действий, которые требуют внимания."],
  delivery: ["Поставка", "Состав MVP-установки и локальный запуск без Docker."],
  jobs: ["Задачи", "Упавшие задачи и ручной retry."],
  results: ["Results", "Поиск и просмотр сохраненных результатов без raw payload в списке."],
  processors: ["Обработчик", "Пассивное состояние PDF-обработчика, очередь, workers и diagnostics."],
  health: ["Проверки", "Локальные readiness checks без внешнего pdf2txt-вызова."],
  storage: ["Storage", "Временное хранилище, лимиты и риск блокировки новых upload-ов."],
  settings: ["Settings", "Read-only runtime-настройки MVP без секретов и строк подключения."],
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

  document.getElementById("audit-filter-form").addEventListener("submit", async event => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    state.auditFilters = {
      action: String(form.get("action") || "").trim(),
      targetType: String(form.get("targetType") || "").trim(),
      targetId: String(form.get("targetId") || "").trim(),
      actor: String(form.get("actor") || "").trim(),
      occurredFrom: String(form.get("occurredFrom") || "").trim(),
      occurredTo: String(form.get("occurredTo") || "").trim(),
      limit: String(form.get("limit") || "50").trim()
    };
    await loadAudit();
    renderAudit();
    renderAuditFilters();
    showAlert("Фильтр аудита применен.");
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
  document.getElementById("refresh-storage-button").addEventListener("click", async () => {
    await loadStorage();
    renderStorageDetails();
    renderOverview();
    showAlert("Storage обновлен.");
  });
  document.getElementById("refresh-settings-button").addEventListener("click", async () => {
    await loadSettings();
    renderSettingsDetails();
    showAlert("Settings обновлены.");
  });
  document.getElementById("refresh-audit-button").addEventListener("click", async () => {
    await loadAudit();
    renderAudit();
    renderAuditFilters();
    showAlert("Аудит обновлен.");
  });
  document.getElementById("audit-filter-reset").addEventListener("click", async () => {
    state.auditFilters = {
      action: "",
      targetType: "",
      targetId: "",
      actor: "",
      occurredFrom: "",
      occurredTo: "",
      limit: "50"
    };
    document.getElementById("audit-filter-form").reset();
    document.querySelector("#audit-filter-form [name='limit']").value = "50";
    await loadAudit();
    renderAudit();
    renderAuditFilters();
    showAlert("Фильтр аудита сброшен.");
  });
  document.getElementById("logout-button").addEventListener("click", logout);
  document.getElementById("retry-detail-button").addEventListener("click", () => {
    if (state.selectedJobDetails) {
      retryJob(state.selectedJobDetails);
    }
  });
  document.getElementById("support-report-button").addEventListener("click", downloadSupportReport);
  document.getElementById("close-job-details-button").addEventListener("click", clearJobDetails);
  document.getElementById("debug-result-payload-button").addEventListener("click", downloadResultPayload);
  document.getElementById("close-result-details-button").addEventListener("click", clearResultDetails);
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
      loadResults(),
      loadProcessor(),
      loadHealth(),
      loadStorage(),
      loadSettings(),
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

async function loadResults() {
  const response = await apiGet("/api/admin/results?limit=50");
  state.results = response.results || [];
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
  const response = await apiGet(`/api/admin/audit?${buildAuditQuery()}`);
  state.audit = response.events || [];
}

function buildAuditQuery() {
  const filters = state.auditFilters || {};
  const query = new URLSearchParams();
  appendAuditFilter(query, "action", filters.action);
  appendAuditFilter(query, "targetType", filters.targetType);
  appendAuditFilter(query, "targetId", filters.targetId);
  appendAuditFilter(query, "actor", filters.actor);
  appendAuditFilter(query, "occurredFrom", toIsoTimestamp(filters.occurredFrom));
  appendAuditFilter(query, "occurredTo", toIsoTimestamp(filters.occurredTo));
  appendAuditFilter(query, "limit", clampAuditLimit(filters.limit));
  return query.toString();
}

function appendAuditFilter(query, name, value) {
  if (value !== null && value !== undefined && String(value).trim() !== "") {
    query.set(name, String(value).trim());
  }
}

function toIsoTimestamp(value) {
  if (!value) {
    return "";
  }
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString();
}

function clampAuditLimit(value) {
  const parsed = Number.parseInt(value, 10);
  if (!Number.isFinite(parsed)) {
    return "50";
  }
  return String(Math.min(200, Math.max(1, parsed)));
}

async function loadStorage() {
  state.storage = await apiGet("/api/admin/storage");
}

async function loadSettings() {
  state.settings = await apiGet("/api/admin/settings");
}

function renderAll() {
  renderOverview();
  renderJobs();
  renderResults();
  renderApiKeys();
  renderUsers();
  renderAudit();
  renderAuditFilters();
  renderJobDetails();
  renderResultDetails();
  renderProcessorDetails();
  renderHealthDetails();
  renderDeliveryDetails();
  renderStorageDetails();
  renderSettingsDetails();
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

function renderResults() {
  const body = document.getElementById("results-body");
  body.innerHTML = "";
  if (state.results.length === 0) {
    appendEmptyRow(body, 7, "Сохраненные результаты еще не найдены.");
    return;
  }

  state.results.forEach(result => {
    const row = document.createElement("tr");
    row.innerHTML = `
      <td><span class="mono">${escapeHtml(shortId(result.resultIndexId))}</span></td>
      <td>${escapeHtml(result.capability)}</td>
      <td><span class="mono">${escapeHtml(result.hash)}</span></td>
      <td>${escapeHtml(result.resultKind)}<br><span class="mono">${escapeHtml(result.contractVersion)}</span></td>
      <td>${formatBytes(result.payloadSize)}</td>
      <td>${formatDate(result.createdAt)}</td>
      <td></td>
    `;
    const openButton = document.createElement("button");
    openButton.className = "secondary-button";
    openButton.type = "button";
    openButton.textContent = "Детали";
    openButton.addEventListener("click", () => loadResultDetails(result.resultIndexId));
    row.lastElementChild.appendChild(openButton);
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

function renderStorageDetails() {
  const temporary = state.storage?.temporary;
  if (!temporary) {
    renderDefinitionList("storage-summary-list", [
      ["Provider", "Нет данных"],
      ["Purpose", "Нет данных"],
      ["Root path", "Нет данных"]
    ]);
    renderDefinitionList("storage-capacity-list", [
      ["Status", "Нет данных"],
      ["Used", "Нет данных"],
      ["Soft limit", "Нет данных"],
      ["Hard limit", "Нет данных"],
      ["Minimum free", "Нет данных"],
      ["Free on volume", "Нет данных"]
    ]);
    setText("storage-status", "Нет данных");
    setText("storage-used", "0 B");
    setText("storage-risk", "Storage еще не загружен.");
    return;
  }

  setText("storage-status", translateStatus(temporary.status));
  setText("storage-used", formatBytes(temporary.usedBytes));
  setText("storage-risk", describeStorageRisk(temporary));

  renderDefinitionList("storage-summary-list", [
    ["Provider", temporary.provider],
    ["Purpose", temporary.purpose],
    ["Root path", temporary.rootPath]
  ]);

  renderDefinitionList("storage-capacity-list", [
    ["Status", translateStatus(temporary.status)],
    ["Used", formatBytes(temporary.usedBytes)],
    ["Soft limit", formatBytes(temporary.softLimitBytes)],
    ["Hard limit", formatBytes(temporary.hardLimitBytes)],
    ["Minimum free", formatBytes(temporary.minimumFreeBytes)],
    ["Free on volume", formatBytes(temporary.availableFreeBytes)]
  ]);
}

function renderSettingsDetails() {
  const settings = state.settings;
  if (!settings) {
    renderDefinitionList("settings-summary-list", [
      ["Max upload", "Нет данных"]
    ]);
    renderDefinitionList("settings-storage-list", [
      ["Temporary root", "Нет данных"],
      ["Soft limit", "Нет данных"],
      ["Hard limit", "Нет данных"],
      ["Minimum free", "Нет данных"]
    ]);
    renderDefinitionList("settings-processor-list", [
      ["Processor key", "Нет данных"],
      ["Capability", "Нет данных"],
      ["Recognizer", "Нет данных"],
      ["Endpoints", "Нет данных"],
      ["Pool concurrency", "Нет данных"],
      ["Endpoint concurrency", "Нет данных"],
      ["Timeout", "Нет данных"],
      ["Max attempts", "Нет данных"],
      ["Overloaded delay", "Нет данных"],
      ["Contract", "Нет данных"]
    ]);
    renderDefinitionList("settings-boundary-list", [
      ["Read only", "Да"],
      ["Editing enabled", "Нет"],
      ["Note", "Settings еще не загружены."]
    ]);
    renderEndpointPool([]);
    return;
  }

  const publicApi = settings.publicApi || {};
  const storage = settings.storage || {};
  const processor = settings.processor || {};
  const boundary = settings.boundary || {};

  renderDefinitionList("settings-summary-list", [
    ["Max upload", formatBytes(publicApi.maxUploadBytes)],
    ["Max upload bytes", publicApi.maxUploadBytes ?? "Нет данных"]
  ]);

  renderDefinitionList("settings-storage-list", [
    ["Temporary root", storage.temporaryRootPath],
    ["Soft limit", formatBytes(storage.temporarySoftLimitBytes)],
    ["Hard limit", formatBytes(storage.temporaryHardLimitBytes)],
    ["Minimum free", formatBytes(storage.temporaryMinimumFreeBytes)]
  ]);

  renderDefinitionList("settings-processor-list", [
    ["Processor key", processor.processorKey],
    ["Capability", processor.capability],
    ["Recognizer", processor.recognizer],
    ["Endpoints", processor.endpointCount ?? 0],
    ["Pool concurrency", processor.poolConcurrencyLimit],
    ["Endpoint concurrency", processor.endpointConcurrencyLimit],
    ["Timeout", processor.timeout],
    ["Max attempts", processor.maxAttempts],
    ["Overloaded delay", processor.processorOverloadedDelay],
    ["Contract", processor.contractVersion]
  ]);

  renderDefinitionList("settings-boundary-list", [
    ["Read only", formatBool(boundary.readOnly)],
    ["Editing enabled", formatBool(boundary.editingEnabled)],
    ["Note", boundary.note]
  ]);

  renderEndpointPool(processor.endpointPool || []);
}

function renderEndpointPool(endpoints) {
  const list = document.getElementById("settings-endpoints-list");
  list.innerHTML = "";
  if (endpoints.length === 0) {
    list.appendChild(workItem("Endpoint pool пуст", "Для Web это допустимо: фактическая обработка выполняется Worker-конфигурацией."));
    return;
  }

  endpoints.forEach((endpoint, index) => {
    list.appendChild(workItem(`Endpoint ${index + 1}`, endpoint));
  });
}

function describeStorageRisk(temporary) {
  if (temporary.status === "full") {
    return "Новые PDF upload-ы будут заблокированы до освобождения места или изменения лимитов.";
  }
  if (temporary.status === "warning") {
    return "Место приближается к soft limit, новые upload-ы пока разрешены.";
  }
  return "Capacity guard не блокирует новые upload-ы.";
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

async function loadResultDetails(resultIndexId) {
  try {
    state.selectedResult = await apiGet(`/api/admin/results/${encodeURIComponent(resultIndexId)}`);
    renderResultDetails();
    showAlert("Детали результата загружены.");
  } catch (error) {
    showAlert(error.message || "Не удалось загрузить детали результата.", true);
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

function renderResultDetails() {
  const panel = document.getElementById("result-details-panel");
  const result = state.selectedResult;
  panel.hidden = !result;
  if (!result) {
    return;
  }

  setText("result-details-title", `Результат ${shortId(result.resultIndexId)}`);
  setText("result-details-subtitle", `${result.capability}: ${result.hash}`);
  const payloadButton = document.getElementById("debug-result-payload-button");
  payloadButton.disabled = result.payloadTable !== "pdf_stamp_recognition_results";
  renderDefinitionList("result-details-meta", [
    ["Result index", result.resultIndexId],
    ["Subject", result.subjectId],
    ["Job", result.jobId],
    ["Capability", result.capability],
    ["Hash", result.hash],
    ["Kind", result.resultKind],
    ["Payload table", result.payloadTable],
    ["Payload id", result.payloadId],
    ["Contract", result.contractVersion],
    ["Payload size", formatBytes(result.payloadSize)],
    ["Created", formatDate(result.createdAt)],
    ["Job status", translateStatus(result.jobStatus)],
    ["Attempt", result.jobAttemptNumber ?? "Нет данных"]
  ]);

  renderResultSummary(result.pdfStampRecognitionSummary);
}

function renderResultSummary(summary) {
  if (!summary) {
    renderDefinitionList("result-details-summary", [
      ["Summary", "Для этого типа результата summary недоступен."],
      ["Raw payload", "Не возвращается этим endpoint-ом."]
    ]);
    return;
  }

  renderDefinitionList("result-details-summary", [
    ["Worker groups", summary.workerGroupCount],
    ["Recognized text items", summary.workerTextItemCount],
    ["Pages with workers", formatList(summary.pageKeys)],
    ["Worker page count", summary.workerPageCount],
    ["Unrecognized pages", summary.unrecognizedPageCount],
    ["Errors", summary.errorCount],
    ["Изм. номер", summary.izmNumber || "Нет данных"],
    ["Error excerpts", formatList(summary.errorExcerpts)]
  ]);
}

function clearResultDetails() {
  state.selectedResult = null;
  renderResultDetails();
}

async function downloadResultPayload() {
  if (!state.selectedResult) {
    showAlert("Сначала откройте детали результата.", true);
    return;
  }

  try {
    const resultIndexId = state.selectedResult.resultIndexId;
    const payload = await apiGet(`/api/admin/results/${encodeURIComponent(resultIndexId)}/payload`);
    const blob = new Blob(
      [JSON.stringify(payload, null, 2)],
      { type: "application/json" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `centerales-result-payload-${shortId(resultIndexId)}.json`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(link.href);
    showAlert("Debug JSON payload скачан.");
  } catch (error) {
    showAlert(error.message || "Не удалось скачать debug payload.", true);
  }
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
    list.appendChild(workItem("Записей аудита нет", "Для выбранного фильтра события не найдены."));
    return;
  }

  state.audit.forEach(event => {
    const item = document.createElement("div");
    item.className = "audit-item";
    item.innerHTML = `
      <strong>${escapeHtml(translateAction(event.action))}</strong>
      <div>${escapeHtml(event.actorLogin || "system")} -> ${escapeHtml(translateAuditTargetType(event.targetType))} / <span class="mono">${escapeHtml(shortAuditTarget(event.targetId))}</span></div>
      <div>${formatDate(event.occurredAt)} · correlationId <span class="mono">${escapeHtml(event.correlationId)}</span></div>
      ${event.comment ? `<div>Комментарий: ${escapeHtml(event.comment)}</div>` : ""}
      <details>
        <summary>Детали</summary>
        <dl class="kv-list audit-safe-details">
          <dt>Audit id</dt>
          <dd><span class="mono">${escapeHtml(event.auditId)}</span></dd>
          <dt>Action</dt>
          <dd>${escapeHtml(event.action)}</dd>
          <dt>Target type</dt>
          <dd>${escapeHtml(event.targetType)}</dd>
          <dt>Target id</dt>
          <dd><span class="mono">${escapeHtml(event.targetId)}</span></dd>
          <dt>Actor</dt>
          <dd>${escapeHtml(event.actorLogin || "system")}</dd>
          <dt>Actor id</dt>
          <dd><span class="mono">${escapeHtml(event.actorAdminId || "Нет данных")}</span></dd>
          <dt>Occurred</dt>
          <dd>${formatDate(event.occurredAt)}</dd>
          <dt>Correlation</dt>
          <dd><span class="mono">${escapeHtml(event.correlationId)}</span></dd>
          <dt>Comment</dt>
          <dd>${escapeHtml(event.comment || "Нет данных")}</dd>
        </dl>
      </details>
    `;
    list.appendChild(item);
  });
}

function renderAuditFilters() {
  const filters = state.auditFilters || {};
  setText("audit-count", `${state.audit.length} ${pluralizeRu(state.audit.length, "событие", "события", "событий")}`);

  const applied = [
    filters.action ? `действие: ${translateAction(filters.action)}` : "",
    filters.targetType ? `объект: ${translateAuditTargetType(filters.targetType)}` : "",
    filters.targetId ? `id: ${filters.targetId}` : "",
    filters.actor ? `actor: ${filters.actor}` : "",
    filters.occurredFrom ? `с: ${filters.occurredFrom}` : "",
    filters.occurredTo ? `по: ${filters.occurredTo}` : "",
    filters.limit && clampAuditLimit(filters.limit) !== "50" ? `limit: ${clampAuditLimit(filters.limit)}` : ""
  ].filter(Boolean);

  setText("audit-filter-summary", applied.length === 0
    ? "Показаны последние события."
    : applied.join(", "));
}

function translateAuditTargetType(targetType) {
  const map = {
    processing_job: "Processing job",
    api_key: "API key",
    admin_user: "Admin user"
  };
  return map[targetType] || targetType || "Нет данных";
}

function shortAuditTarget(value) {
  if (!value) {
    return "Нет данных";
  }
  return value.length > 16 ? `${value.slice(0, 8)}...${value.slice(-4)}` : value;
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
