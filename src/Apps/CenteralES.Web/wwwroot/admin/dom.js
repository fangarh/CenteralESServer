(function () {
  const { escapeHtml } = window.CenteralESAdminFormatters;

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

  function setText(id, value) {
    document.getElementById(id).textContent = value;
  }

  window.CenteralESAdminDom = {
    showAlert,
    showCreatedSecret,
    workItem,
    appendEmptyRow,
    detailBlock,
    renderDefinitionList,
    setText
  };
})();
