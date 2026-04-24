const state = {
  data: null,
  selectedIndex: 0
};

const elements = {
  latestStatus: document.querySelector("#latest-status"),
  runCount: document.querySelector("#run-count"),
  latestModel: document.querySelector("#latest-model"),
  sqlStatus: document.querySelector("#sql-status"),
  updatedAt: document.querySelector("#updated-at"),
  runs: document.querySelector("#runs"),
  selectedRun: document.querySelector("#selected-run"),
  statusSummary: document.querySelector("#status-summary"),
  whatJulesDid: document.querySelector("#what-jules-did"),
  nextPrompt: document.querySelector("#next-prompt"),
  databasePlan: document.querySelector("#database-plan"),
  events: document.querySelector("#events"),
  reloadButton: document.querySelector("#reload-button"),
  fileInput: document.querySelector("#file-input")
};

async function loadDashboard() {
  if (window.AGENT_DASHBOARD) {
    setData(window.AGENT_DASHBOARD);
    return;
  }

  try {
    const response = await fetch(`data/dashboard.json?ts=${Date.now()}`);
    if (!response.ok) {
      throw new Error(`HTTP ${response.status}`);
    }

    const data = await response.json();
    setData(data);
  } catch (error) {
    showEmpty(`data/dashboard.json okunamadi. Ajan tek tur calistir veya JSON Sec ile dosya yukle. (${error.message})`);
  }
}

function setData(data) {
  state.data = data;
  state.selectedIndex = 0;
  render();
}

function render() {
  const runs = getRuns();
  const latest = runs[0];

  elements.runCount.textContent = String(runs.length);
  elements.updatedAt.textContent = getValue(state.data, "updatedAt") ? formatDate(getValue(state.data, "updatedAt")) : "-";
  elements.latestStatus.textContent = latest?.status ?? "-";
  elements.latestModel.textContent = latest?.model ?? "-";
  elements.sqlStatus.textContent = latest?.sqlReportMessage ?? "-";

  elements.runs.innerHTML = "";
  runs.forEach((run, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `run-card${index === state.selectedIndex ? " active" : ""}`;
    button.innerHTML = `
      <strong>${escapeHtml(run.statusSummary || run.status || "Run")}</strong>
      <span>${escapeHtml(formatDate(run.createdAt))}</span>
      <span>${escapeHtml(run.model || "-")}</span>
    `;
    button.addEventListener("click", () => {
      state.selectedIndex = index;
      render();
    });
    elements.runs.appendChild(button);
  });

  renderDetail(runs[state.selectedIndex]);
}

function renderDetail(run) {
  if (!run) {
    showEmpty("Run yok.");
    return;
  }

  elements.selectedRun.textContent = formatDate(run.createdAt);
  elements.statusSummary.textContent = run.statusSummary || "-";
  elements.whatJulesDid.textContent = run.whatJulesDid || "-";
  elements.nextPrompt.textContent = run.nextPrompt || "-";
  elements.databasePlan.textContent = run.databasePlan || "-";

  elements.events.innerHTML = "";
  (getValue(run, "events") ?? []).forEach((event) => {
    const item = document.createElement("div");
    item.className = `event ${event.severity || "info"}`;
    item.innerHTML = `
      <small>${escapeHtml(formatDate(event.createdAt))} - ${escapeHtml(event.eventType || "event")} - ${escapeHtml(event.severity || "info")}</small>
      <div>${escapeHtml(event.message || "")}</div>
    `;
    elements.events.appendChild(item);
  });
}

function showEmpty(message) {
  elements.latestStatus.textContent = "-";
  elements.runCount.textContent = "0";
  elements.latestModel.textContent = "-";
  elements.sqlStatus.textContent = "-";
  elements.updatedAt.textContent = "-";
  elements.runs.innerHTML = "";
  elements.selectedRun.textContent = "run yok";
  elements.statusSummary.textContent = message;
  elements.whatJulesDid.textContent = "-";
  elements.nextPrompt.textContent = "-";
  elements.databasePlan.textContent = "-";
  elements.events.innerHTML = "";
}

function getRuns() {
  return getValue(state.data, "runs") ?? [];
}

function getValue(source, camelName) {
  if (!source) {
    return undefined;
  }

  const pascalName = camelName.charAt(0).toUpperCase() + camelName.slice(1);
  return source[camelName] ?? source[pascalName];
}

function formatDate(value) {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat("tr-TR", {
    dateStyle: "short",
    timeStyle: "medium"
  }).format(date);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

elements.reloadButton.addEventListener("click", loadDashboard);
elements.fileInput.addEventListener("change", async (event) => {
  const file = event.target.files?.[0];
  if (!file) {
    return;
  }

  const text = await file.text();
  setData(JSON.parse(text));
});

loadDashboard();
