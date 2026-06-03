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
    const credential = `${keyId}.${secret}`;
    box.hidden = false;
    box.innerHTML = "";

    const title = document.createElement("strong");
    title.textContent = `Ключ ${keyId} показан один раз.`;

    const value = document.createElement("div");
    value.className = "mono secret-value";
    value.textContent = credential;

    const copyButton = document.createElement("button");
    copyButton.type = "button";
    copyButton.className = "secondary-button secret-copy-button";
    copyButton.textContent = "Скопировать ключ";
    copyButton.addEventListener("click", async () => {
      await copyText(credential);
      showAlert("Ключ скопирован.");
    });

    box.append(title, value, copyButton);
  }

  async function copyText(value) {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(value);
      return;
    }

    const input = document.createElement("textarea");
    input.value = value;
    input.setAttribute("readonly", "");
    input.style.position = "fixed";
    input.style.opacity = "0";
    document.body.appendChild(input);
    input.select();
    document.execCommand("copy");
    input.remove();
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
