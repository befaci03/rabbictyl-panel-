// Register Account add-on script.
//
// On every administration page (except Users, which already ships a create
// form) a "Register Account" button is added to the page header. It opens a
// dialog whose form performs a normal POST to /admin/users/create, so the
// panel's own TempData success/error alerts are used. The antiforgery token is
// taken from a form on the current page, or fetched from /admin/users when the
// page has none.
//
// Addon scripts are classic scripts sharing one global scope, so everything
// runs inside an IIFE to avoid clashing with other add-ons.

(() => {
interface AddonApi {
  ready(cb: () => void): void;
}

const ADDON_ID = "reg_user";
const TOKEN_NAME = "__RequestVerificationToken";

const addons = (window as unknown as { Rabbictyl: { forAddon(id: string): AddonApi } }).Rabbictyl;
const api = addons.forAddon(ADDON_ID);

function isAdminPage(): boolean {
  const path = window.location.pathname.toLowerCase();
  return path === "/admin" || path.startsWith("/admin/");
}

function isUsersPage(): boolean {
  return window.location.pathname.toLowerCase().replace(/\/+$/, "") === "/admin/users";
}

function pageToken(): string | null {
  const input = document.querySelector<HTMLInputElement>(`input[name="${TOKEN_NAME}"]`);
  return input && input.value ? input.value : null;
}

async function resolveToken(): Promise<string | null> {
  const existing = pageToken();

  if (existing) {
    return existing;
  }

  try {
    const response = await fetch("/admin/users", { credentials: "same-origin" });

    if (!response.ok) {
      return null;
    }

    const doc = new DOMParser().parseFromString(await response.text(), "text/html");
    const input = doc.querySelector<HTMLInputElement>(`input[name="${TOKEN_NAME}"]`);
    return input && input.value ? input.value : null;
  } catch {
    return null;
  }
}

function field(label: string, name: string, options: { type?: string; required?: boolean; hint?: string; maxLength?: number; minLength?: number } = {}): string {
  const type = options.type ?? "text";
  const required = options.required ? " required" : "";
  const maxLength = options.maxLength ? ` maxlength="${options.maxLength}"` : "";
  const minLength = options.minLength ? ` minlength="${options.minLength}"` : "";
  const autocomplete = type === "password" ? ' autocomplete="new-password"' : ' autocomplete="off"';

  return (
    `<div class="form__group">` +
    `<label class="form__label" for="reg-user-${name}">${label}</label>` +
    `<input class="form__input" id="reg-user-${name}" name="${name}" type="${type}"${required}${minLength}${maxLength}${autocomplete} />` +
    (options.hint ? `<span class="form__hint">${options.hint}</span>` : "") +
    `</div>`
  );
}

function buildDialog(): { overlay: HTMLElement; form: HTMLFormElement; status: HTMLElement; token: HTMLInputElement } {
  const overlay = document.createElement("div");
  overlay.className = "reg-user-overlay";
  overlay.dataset.regUser = "dialog";
  Object.assign(overlay.style, {
    position: "fixed",
    inset: "0",
    background: "rgba(0, 0, 0, .55)",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    zIndex: "1000",
    padding: "16px",
  });

  overlay.innerHTML =
    `<div class="card reg-user-dialog" role="dialog" aria-modal="true" aria-labelledby="reg-user-title" style="width: min(460px, 100%); max-height: 90vh; overflow: auto;">` +
    `<div class="card__header" style="display: flex; align-items: center; justify-content: space-between; gap: 12px;">` +
    `<h2 class="card__title" id="reg-user-title">Register Account</h2>` +
    `<button type="button" class="button button--ghost button--sm" data-reg-user="close">Close</button>` +
    `</div>` +
    `<form class="form" method="post" action="/admin/users/create" style="padding-top: 8px;">` +
    `<input type="hidden" name="${TOKEN_NAME}" value="" />` +
    `<div class="admin-create__grid" style="display: grid; gap: 12px;">` +
    field("First name", "FirstName", { required: true, maxLength: 64 }) +
    field("Last name (optional)", "LastName", { maxLength: 64 }) +
    field("Username", "Username", { required: true, minLength: 3, maxLength: 32 }) +
    field("Email", "Email", { type: "email", required: true, maxLength: 255 }) +
    field("Password", "Password", { type: "password", required: true, minLength: 8, maxLength: 128 }) +
    `</div>` +
    `<div class="admin-create__checks" style="margin-top: 12px;">` +
    `<label class="form__check"><input type="checkbox" name="IsRootAdmin" value="true" /> Grant administrator access</label>` +
    `</div>` +
    `<div class="admin-create__actions" style="display: flex; gap: 8px; margin-top: 16px;">` +
    `<button type="submit" class="button button--primary" data-reg-user="submit">Register account</button>` +
    `<button type="button" class="button button--ghost" data-reg-user="close">Cancel</button>` +
    `</div>` +
    `<p class="form__hint" data-reg-user="status" style="margin-top: 10px;"></p>` +
    `</form>` +
    `</div>`;

  const form = overlay.querySelector<HTMLFormElement>("form")!;
  const status = overlay.querySelector<HTMLElement>('[data-reg-user="status"]')!;
  const token = overlay.querySelector<HTMLInputElement>(`input[name="${TOKEN_NAME}"]`)!;

  return { overlay, form, status, token };
}

function inject(): void {
  const header = document.querySelector<HTMLElement>(".page__header");

  if (!header || document.querySelector('[data-reg-user="open"]')) {
    return;
  }

  const { overlay, form, status, token } = buildDialog();

  const open = document.createElement("button");
  open.type = "button";
  open.className = "button button--primary button--sm";
  open.dataset.regUser = "open";
  open.textContent = "Register Account";
  open.style.marginLeft = "auto";
  open.style.flex = "none";
  header.append(open);

  document.body.append(overlay);

  const close = (): void => {
    overlay.style.display = "none";
    status.textContent = "";
  };

  open.addEventListener("click", () => {
    overlay.style.display = "flex";
    form.querySelector<HTMLInputElement>("input[name=FirstName]")?.focus();
  });

  for (const button of overlay.querySelectorAll<HTMLElement>('[data-reg-user="close"]')) {
    button.addEventListener("click", close);
  }

  overlay.addEventListener("click", (event) => {
    if (event.target === overlay) {
      close();
    }
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && overlay.style.display !== "none") {
      close();
    }
  });

  const submit = overlay.querySelector<HTMLElement>('[data-reg-user="submit"]')!;

  submit.addEventListener("click", async (event) => {
    event.preventDefault();

    if (!form.reportValidity()) {
      return;
    }

    status.textContent = "Checking security token…";
    const value = await resolveToken();

    if (!value) {
      status.textContent = "Could not load a security token. Open Administration → Users and try again.";
      return;
    }

    token.value = value;
    status.textContent = "Creating account…";
    form.requestSubmit();
  });
}

api.ready(() => {
  if (!isAdminPage() || isUsersPage()) {
    return;
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", inject, { once: true });
  } else {
    inject();
  }
});
})();
