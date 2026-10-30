// Egg Banners: asks the add-on for the banner that matches this server's egg
// and drops it into the slot the server page exposes.

const slot = document.querySelector<HTMLElement>('[data-addon-slot="server.banner"]');
const uuid = slot?.dataset.serverUuid;

if (slot && uuid) {
  try {
    const response = await fetch(
      `/addons/egg_banners/api/banner?server=${encodeURIComponent(uuid)}`,
      { credentials: "same-origin" },
    );

    if (response.ok) {
      const blob = await response.blob();
      const image = document.createElement("img");

      image.src = URL.createObjectURL(blob);
      image.alt = "";
      image.decoding = "async";

      slot.append(image);
    }
  } catch {
    // No banner for this egg, or the add-on is disabled: leave the slot empty.
  }
}

export {};
