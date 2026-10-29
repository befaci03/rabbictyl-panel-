// Pterodactyl Blue: lift the topbar into a raised state while scrolling.
const topbar = document.querySelector<HTMLElement>(".topbar");

if (topbar) {
  const sync = (): void => {
    topbar.classList.toggle("is-scrolled", window.scrollY > 4);
  };

  sync();
  window.addEventListener("scroll", sync, { passive: true });
}

document.documentElement.dataset.themeScript = "pterodactyl_blue";

export {};
