import { connectRealtime } from "./realtime";

const authenticated = document.body.dataset.authenticated === "true";
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
document.documentElement.classList.toggle("motion-ok", !reduceMotion);

const shell = document.querySelector<HTMLElement>("#app-shell");
const sidebarToggle = document.querySelector<HTMLButtonElement>("#sidebar-toggle");
const mobileQuery = window.matchMedia("(max-width: 900px)");
const storageKey = "rabbictyl.sidebar.compact";
const supportsCompact = !document.body.classList.contains("ptero-body");
const collapseLabel = sidebarToggle?.dataset.labelCollapse ?? "Collapse sidebar";
const expandLabel = sidebarToggle?.dataset.labelExpand ?? "Expand sidebar";
const openLabel = sidebarToggle?.dataset.labelOpen ?? "Open navigation";
const closeLabel = sidebarToggle?.dataset.labelClose ?? "Close navigation";

const isMobile = (): boolean => mobileQuery.matches;

function syncSidebarButton(): void {
  if (!sidebarToggle) {
    return;
  }

  if (isMobile()) {
    const open = shell?.classList.contains("is-sidebar-open") ?? false;
    sidebarToggle.title = open ? closeLabel : openLabel;
    sidebarToggle.setAttribute("aria-label", sidebarToggle.title);
    sidebarToggle.setAttribute("aria-expanded", String(open));
    return;
  }

  const compact = shell?.classList.contains("is-compact") ?? false;
  sidebarToggle.title = compact ? expandLabel : collapseLabel;
  sidebarToggle.setAttribute("aria-label", sidebarToggle.title);
  sidebarToggle.setAttribute("aria-expanded", String(!compact));
}

function applyCompact(compact: boolean): void {
  if (!shell || !supportsCompact) {
    return;
  }

  shell.classList.toggle("is-compact", compact && !isMobile());
  syncSidebarButton();
}

function closeSidebar(): void {
  shell?.classList.remove("is-sidebar-open");
  syncSidebarButton();
}

if (shell && sidebarToggle) {
  let compact = false;

  try {
    compact = window.localStorage.getItem(storageKey) === "true";
  } catch {
    // Storage can be unavailable in private browsing; the toggle still works.
  }

  applyCompact(compact);

  sidebarToggle.addEventListener("click", () => {
    if (isMobile()) {
      shell.classList.toggle("is-sidebar-open");
      syncSidebarButton();
      return;
    }

    const next = !shell.classList.contains("is-compact");
    applyCompact(next);

    try {
      window.localStorage.setItem(storageKey, String(next));
    } catch {
      // Ignore storage failures.
    }
  });

  document.querySelectorAll<HTMLElement>("[data-sidebar-close]").forEach((backdrop) => {
    backdrop.addEventListener("click", closeSidebar);
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      closeSidebar();
    }
  });

  mobileQuery.addEventListener("change", () => {
    closeSidebar();
    applyCompact(compact);
  });
}

const localeSelect = document.querySelector<HTMLSelectElement>("#locale-select");

if (localeSelect) {
  localeSelect.addEventListener("change", () => {
    const culture = localeSelect.value;
    const value = encodeURIComponent(`c=${culture}|uic=${culture}`);
    document.cookie = `.AspNetCore.Culture=${value}; path=/; max-age=31536000; samesite=lax`;
    window.location.reload();
  });
}

const csrfToken = document.querySelector<HTMLMetaElement>('meta[name="csrf-token"]')?.content ?? "";

async function postJson(url: string, body: unknown): Promise<Response> {
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-CSRF-TOKEN": csrfToken,
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;

    try {
      const payload = (await response.json()) as { error?: string; title?: string };
      message = payload.error ?? payload.title ?? message;
    } catch {
      // Keep the status-based message.
    }

    throw new Error(message);
  }

  return response;
}

const consoleElement = document.querySelector<HTMLElement>("[data-server-console]");

if (consoleElement) {
  consoleElement.scrollTop = consoleElement.scrollHeight;
}

document.querySelectorAll<HTMLElement>("[data-server-power]").forEach((group) => {
  group.addEventListener("click", (event) => {
    const button = (event.target as HTMLElement).closest<HTMLButtonElement>("[data-power-action]");
    const serverUuid = group.dataset.serverUuid;

    if (!button || !serverUuid) {
      return;
    }

    const action = button.dataset.powerAction;
    if (!action) {
      return;
    }

    void (async () => {
      button.disabled = true;
      group.classList.add("is-busy");

      try {
        await postJson(`/api/client/servers/${serverUuid}/power`, { action });
        window.setTimeout(() => window.location.reload(), 900);
      } catch (error) {
        window.alert(error instanceof Error ? error.message : "Could not send the power action.");
      } finally {
        button.disabled = false;
        group.classList.remove("is-busy");
      }
    })();
  });
});

document.querySelectorAll<HTMLFormElement>("form[data-server-command]").forEach((form) => {
  form.addEventListener("submit", (event) => {
    event.preventDefault();

    const serverUuid = form.dataset.serverUuid;
    const input = form.querySelector<HTMLInputElement>('input[name="command"]');
    const command = input?.value.trim();

    if (!serverUuid || !command) {
      return;
    }

    void (async () => {
      const submit = form.querySelector<HTMLButtonElement>('button[type="submit"]');
      if (submit) {
        submit.disabled = true;
      }

      try {
        await postJson(`/api/client/servers/${serverUuid}/command`, { command });

        if (consoleElement) {
          const prefix = consoleElement.textContent?.endsWith("\n") ? "" : "\n";
          consoleElement.textContent = `${consoleElement.textContent ?? ""}${prefix}> ${command}\n`;
          consoleElement.scrollTop = consoleElement.scrollHeight;
        }

        if (input) {
          input.value = "";
        }
      } catch (error) {
        window.alert(error instanceof Error ? error.message : "Could not send the command.");
      } finally {
        if (submit) {
          submit.disabled = false;
        }
      }
    })();
  });
});

interface FileEntry {
  name?: string;
  size?: number;
  is_file?: boolean;
  is_symlink?: boolean;
  type?: string;
  modified_at?: string;
}

interface FileListPayload {
  data?: FileEntry[];
}

const fileBrowser = document.querySelector<HTMLElement>("[data-file-browser]");

if (fileBrowser) {
  const serverUuid = fileBrowser.dataset.serverUuid;
  const rows = fileBrowser.querySelector<HTMLElement>("[data-file-rows]");
  const pathLabel = fileBrowser.querySelector<HTMLElement>("[data-file-path]");
  const upButton = fileBrowser.querySelector<HTMLButtonElement>("[data-file-up]");
  let currentPath = "/";

  const joinPath = (base: string, name: string): string => {
    if (name.startsWith("/")) {
      return name;
    }

    return base === "/" ? `/${name}` : `${base.replace(/\/$/, "")}/${name}`;
  };

  const parentPath = (path: string): string => {
    const parent = path.replace(/\/[^/]+\/?$/, "");
    return parent === "" ? "/" : parent;
  };

  const formatSize = (size?: number): string => {
    if (typeof size !== "number" || size < 0) {
      return "—";
    }

    if (size < 1024) {
      return `${size} B`;
    }

    const units = ["KB", "MB", "GB", "TB"];
    let value = size / 1024;
    let unit = 0;

    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit += 1;
    }

    return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
  };

  const renderMessage = (message: string): void => {
    if (!rows) {
      return;
    }

    const row = document.createElement("tr");
    const cell = document.createElement("td");
    cell.colSpan = 3;
    cell.className = "table__secondary";
    cell.textContent = message;
    row.append(cell);
    rows.replaceChildren(row);
  };

  const load = async (path: string): Promise<void> => {
    if (!serverUuid || !rows) {
      return;
    }

    currentPath = path.startsWith("/") ? path : `/${path}`;
    if (pathLabel) {
      pathLabel.textContent = currentPath;
    }
    renderMessage(fileBrowser.dataset.loadingLabel ?? "Loading directory…");

    try {
      const response = await fetch(
        `/api/client/servers/${serverUuid}/files/list?directory=${encodeURIComponent(currentPath)}`,
        { headers: { Accept: "application/json" } },
      );

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const payload = (await response.json()) as FileEntry[] | FileListPayload;
      const entries = Array.isArray(payload)
        ? payload
        : Array.isArray(payload.data)
          ? payload.data
          : [];

      if (entries.length === 0) {
        renderMessage(fileBrowser.dataset.emptyLabel ?? "This directory is empty.");
        return;
      }

      rows.replaceChildren();

      for (const entry of entries) {
        const name = String(entry.name ?? "").trim();
        if (!name || name === "." || name === "..") {
          continue;
        }

        const isDirectory = entry.is_file === false || entry.type === "directory";
        const row = document.createElement("tr");
        const nameCell = document.createElement("td");
        const nameButton = document.createElement("button");
        const icon = document.createElement("span");
        const text = document.createElement("span");

        nameButton.type = "button";
        nameButton.className = "file-row__name";
        icon.className = "file-row__icon";
        icon.textContent = isDirectory ? "DIR" : "FILE";
        text.textContent = name;
        nameButton.append(icon, text);

        if (isDirectory) {
          nameButton.addEventListener("click", () => {
            void load(joinPath(currentPath, name));
          });
        } else {
          nameButton.classList.add("is-file");
        }

        nameCell.append(nameButton);

        const sizeCell = document.createElement("td");
        sizeCell.className = "table__number";
        sizeCell.textContent = isDirectory ? "—" : formatSize(entry.size);

        const modifiedCell = document.createElement("td");
        modifiedCell.className = "table__secondary";
        modifiedCell.textContent = entry.modified_at ?? "—";

        row.append(nameCell, sizeCell, modifiedCell);
        rows.append(row);
      }
    } catch {
      renderMessage(fileBrowser.dataset.errorLabel ?? "Could not load this directory from the daemon.");
    }
  };

  upButton?.addEventListener("click", () => {
    void load(parentPath(currentPath));
  });

  void load("/");
}

if (authenticated) {
  void connectRealtime().catch((error: unknown) => {
    // Realtime is an enhancement; never break the page when it is unavailable.
    console.warn("[realtime] could not connect", error);
  });
}
