// The browser half of the add-on API. Add-on scripts are plain ES modules
// bundled with esbuild; this object is the only surface they need.

export interface AddonInstance {
  readonly id: string;
  /** First element with `data-addon-slot="{name}"`, if the page exposes one. */
  slot(name: string): HTMLElement | null;
  /** Fetch an endpoint owned by this add-on: `/addons/{id}/api/{path}`. */
  api(path: string, init?: RequestInit): Promise<Response>;
  /** Fetch JSON from one of the add-on's endpoints. */
  json<T>(path: string, init?: RequestInit): Promise<T>;
  /** Show a short message in the panel's toast stack. */
  toast(message: string): void;
  /** Run once the document is parsed. */
  ready(callback: () => void): void;
}

export interface AddonApi {
  /** Bind the API to the add-on whose manifest id this is. */
  forAddon(id: string): AddonInstance;
  slot(name: string): HTMLElement | null;
  toast(message: string): void;
}

function slot(name: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-addon-slot="${name}"]`);
}

function toastRegion(): HTMLElement {
  const existing = document.querySelector<HTMLElement>("[data-addon-toasts]");

  if (existing) {
    return existing;
  }

  const region = document.createElement("div");
  region.className = "addon-toasts";
  region.dataset.addonToasts = "";
  document.body.append(region);

  return region;
}

function toast(message: string): void {
  const element = document.createElement("div");
  element.className = "addon-toast";
  element.setAttribute("role", "status");
  element.textContent = message;

  toastRegion().append(element);
  window.setTimeout(() => element.remove(), 4000);
}

function forAddon(id: string): AddonInstance {
  const base = `/addons/${encodeURIComponent(id)}/api`;

  const api = (path: string, init?: RequestInit): Promise<Response> =>
    fetch(`${base}/${path.replace(/^\//, "")}`, {
      credentials: "same-origin",
      ...init,
    });

  return {
    id,
    slot,
    api,
    toast,
    async json<T>(path: string, init?: RequestInit): Promise<T> {
      const response = await api(path, init);

      if (!response.ok) {
        throw new Error(`Add-on request failed with HTTP ${response.status}.`);
      }

      return (await response.json()) as T;
    },
    ready(callback: () => void) {
      if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", callback, { once: true });
      } else {
        callback();
      }
    },
  };
}

const api: AddonApi = { forAddon, slot, toast };

(window as unknown as { Rabbictyl: AddonApi }).Rabbictyl = api;

export default api;
