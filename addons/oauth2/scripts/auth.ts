// OAuth2 add-on: adds this add-on's sign-in buttons to the panel's generic
// sign-in slot. The panel itself has no OAuth2 knowledge.

const slot = document.querySelector<HTMLElement>('[data-addon-slot="auth.providers"]');

if (slot) {
  try {
    const response = await fetch("/addons/oauth2/api/providers", {
      headers: { Accept: "application/json" },
      credentials: "same-origin",
    });

    if (response.ok) {
      const providers = (await response.json()) as Array<{ id: string; name: string }>;

      if (providers.length > 0) {
        const label = document.createElement("span");
        label.className = "form__label";
        label.textContent = "or";
        slot.append(label);

        for (const provider of providers) {
          const link = document.createElement("a");
          link.className = "button button--ghost button--block";
          link.href = `/addons/oauth2/api/start?provider=${encodeURIComponent(provider.id)}`;
          link.textContent = `Continue with ${provider.name}`;
          slot.append(link);
        }
      }
    }
  } catch {
    // Provider discovery is optional; leave the slot empty when it fails.
  }
}

export {};
