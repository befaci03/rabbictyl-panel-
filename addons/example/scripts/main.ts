// Example addon: prove the manifest, script and asset APIs all work.
interface AddonManifestResponse {
  name: string;
  assets: Record<string, string>;
}

const ADDON_ID = "example";

async function loadAddon(): Promise<void> {
  const topbar = document.querySelector<HTMLElement>(".topbar__user");

  if (!topbar || document.querySelector(".addon-badge")) {
    return;
  }

  const response = await fetch(`/api/addons/${ADDON_ID}`, {
    headers: { Accept: "application/json" },
  });

  if (!response.ok) {
    return;
  }

  const manifest = (await response.json()) as AddonManifestResponse;
  const badge = document.createElement("span");
  badge.className = "addon-badge";
  badge.title = "Loaded from the example addon";

  const iconUrl = manifest.assets["icon"];

  if (iconUrl) {
    const icon = document.createElement("img");
    icon.src = iconUrl;
    icon.alt = "";
    icon.width = 14;
    icon.height = 14;
    badge.append(icon);
  }

  badge.append(document.createTextNode(manifest.name));
  topbar.prepend(badge);
}

void loadAddon().catch((error: unknown) => {
  console.warn("[addon:example] could not load", error);
});

export {};
