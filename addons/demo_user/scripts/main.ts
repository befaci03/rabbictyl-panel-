// Demo User add-on script.
//
// The add-on never creates nodes, servers or allocations. When the signed-in
// account is the demo account it renders two simulated servers on the dashboard,
// purely in the browser: statuses rotate, a fake console streams, nothing is
// written back to the panel.

interface DemoState {
  isDemoUser: boolean;
  username: string;
}

interface SimulatedServer {
  name: string;
  egg: string;
  description: string;
  address: string;
  memory: string;
  disk: string;
  status: ServerStatus;
  log: string[];
}

type ServerStatus = "Running" | "Starting" | "Stopping" | "Offline";

const addons = (window as unknown as {
  Rabbictyl: { forAddon(id: string): { json<T>(path: string): Promise<T>; ready(cb: () => void): void } };
}).Rabbictyl;

const api = addons.forAddon("demo_user");

const STATUS_BADGE: Record<ServerStatus, string> = {
  Running: "badge--running",
  Starting: "badge--starting",
  Stopping: "badge--stopping",
  Offline: "badge--offline",
};

const LOG_POOL = [
  "[Server thread/INFO]: Starting minecraft server version 1.21.4",
  "[Server thread/INFO]: Loading properties",
  "[Server thread/INFO]: Preparing level \"world\"",
  "[Server thread/INFO]: Done (3.214s)! For help, type \"help\"",
  "[Server thread/INFO]: Steve joined the game",
  "[Server thread/INFO]: Alex joined the game",
  "[Server thread/INFO]: Steve left the game",
  "[Server thread/INFO]: Saving chunks for level 'ServerLevel[world]'",
];

function element<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  className?: string,
  text?: string,
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);

  if (className) {
    node.className = className;
  }

  if (text !== undefined) {
    node.textContent = text;
  }

  return node;
}

function isDashboard(): boolean {
  const path = window.location.pathname.replace(/\/+$/, "") || "/";

  return path === "/" || path.toLowerCase() === "/home";
}

function simulateServers(): SimulatedServer[] {
  return [
    {
      name: "Demo Server",
      egg: "Minecraft",
      description: "Simulated Minecraft server, rendered by the Demo User add-on.",
      address: "127.0.0.1:25565",
      memory: "1024 MB",
      disk: "10240 MB",
      status: "Running",
      log: LOG_POOL.slice(0, 5),
    },
    {
      name: "Demo Server 2",
      egg: "Terraria",
      description: "Simulated Terraria server, rendered by the Demo User add-on.",
      address: "127.0.0.1:25566",
      memory: "1024 MB",
      disk: "10240 MB",
      status: "Offline",
      log: [],
    },
  ];
}

function render(): void {
  const page = document.querySelector<HTMLElement>(".content > .page");

  if (!page) {
    return;
  }

  const servers = simulateServers();
  const grid = element("div", "server-grid");
  grid.dataset.demoSimulation = "servers";

  const empty = page.querySelector<HTMLElement>(".empty");

  if (empty) {
    empty.remove();
  }

  const consolePanel = element("section", "card");
  consolePanel.dataset.demoSimulation = "console";
  consolePanel.style.marginTop = "20px";
  consolePanel.style.display = "none";

  const consoleHeader = element("div", "card__header");
  const consoleTitle = element("h2", "card__title", "Demo Server · console");
  const consoleClose = element("button", "button button--ghost button--sm", "Close");
  consoleClose.type = "button";
  consoleHeader.append(consoleTitle, consoleClose);

  const consoleOutput = element("pre");
  consoleOutput.style.margin = "0";
  consoleOutput.style.padding = "16px";
  consoleOutput.style.maxHeight = "260px";
  consoleOutput.style.overflow = "auto";
  consoleOutput.style.fontFamily = "var(--font-mono, ui-monospace, monospace)";
  consoleOutput.style.fontSize = "12.5px";
  consoleOutput.style.lineHeight = "1.7";
  consoleOutput.style.background = "var(--surface-sunken, rgba(0, 0, 0, .35))";
  consoleOutput.style.borderRadius = "10px";
  consoleOutput.textContent = "(console output is simulated in your browser)";

  consolePanel.append(consoleHeader, consoleOutput);
  consoleClose.addEventListener("click", () => {
    consolePanel.style.display = "none";
  });

  const cards = servers.map((server, index) => {
    const card = element("button", "server-card card");
    card.type = "button";
    card.style.textAlign = "left";
    card.style.width = "100%";
    card.style.cursor = "pointer";

    const top = element("div", "server-card__top");
    const icon = element("span", "server-card__icon");
    icon.innerHTML =
      '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><rect x="3" y="4" width="18" height="6" rx="2"/><rect x="3" y="14" width="18" height="6" rx="2"/><path d="M7 7h.01M7 17h.01"/></svg>';

    const badge = element("span", "badge");
    top.append(icon, badge);

    const name = element("h2", "server-card__name", server.name);
    const description = element("p", "server-card__description", server.description);

    const meta = element("dl", "server-card__meta");

    for (const [label, value] of [
      ["Egg", server.egg],
      ["Address", server.address],
      ["Memory", server.memory],
      ["Disk", server.disk],
    ]) {
      const row = element("div");
      row.append(element("dt", undefined, label), element("dd", undefined, value));
      meta.append(row);
    }

    const open = element("span", "server-card__open", "Open console →");

    const body = element("div");
    body.append(name, description);

    card.append(top, body, meta, open);

    const showConsole = (): void => {
      consoleTitle.textContent = `${server.name} · console`;
      consoleOutput.textContent =
        server.log.length > 0
          ? server.log.join("\n")
          : "(server is offline — simulated in your browser)";
      consolePanel.style.display = "";
      consolePanel.scrollIntoView({ behavior: "smooth", block: "nearest" });
    };

    card.addEventListener("click", showConsole);
    open.addEventListener("click", (event) => {
      event.preventDefault();
      showConsole();
    });

    grid.append(card);

    return { server, badge, index };
  });

  const note = element(
    "p",
    "form__hint",
    "These two servers are simulated in your browser by the Demo User add-on. Nothing is stored in the panel.",
  );
  note.style.marginTop = "12px";

  page.append(grid, note, consolePanel);

  const cycle: ServerStatus[] = ["Offline", "Starting", "Running", "Stopping"];
  let tick = 0;

  window.setInterval(() => {
    const target = cards[1];
    target.server.status = cycle[tick % cycle.length];
    tick++;

    if (target.server.status === "Running" && target.server.log.length === 0) {
      target.server.log = LOG_POOL.slice(0, 4);
    }

    if (target.server.status === "Offline") {
      target.server.log = [];
    }

    if (target.server.status === "Starting") {
      target.server.log = LOG_POOL.slice(0, 2);
    }

    target.badge.textContent = target.server.status;
    target.badge.className = `badge ${STATUS_BADGE[target.server.status]}`;
  }, 6000);

  for (const { server, badge } of cards) {
    badge.textContent = server.status;
    badge.className = `badge ${STATUS_BADGE[server.status]}`;
  }
}

api.ready(() => {
  if (!isDashboard()) {
    return;
  }

  api
    .json<DemoState>("state")
    .then((state) => {
      if (state.isDemoUser) {
        render();
      }
    })
    .catch(() => {
      // Add-on endpoints are unavailable while the add-on is disabled.
    });
});
