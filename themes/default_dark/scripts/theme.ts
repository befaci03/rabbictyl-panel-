// Default Dark: count the dashboard stats up once when the page loads.
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

if (!reduceMotion) {
  document.querySelectorAll<HTMLElement>(".stat__value").forEach((element) => {
    const target = Number.parseInt(element.textContent ?? "", 10);
    if (!Number.isFinite(target) || target <= 0) {
      return;
    }

    const duration = 550;
    const start = performance.now();
    element.textContent = "0";

    const tick = (now: number): void => {
      const progress = Math.min((now - start) / duration, 1);
      const eased = 1 - Math.pow(1 - progress, 3);
      element.textContent = String(Math.round(target * eased));

      if (progress < 1) {
        requestAnimationFrame(tick);
      }
    };

    requestAnimationFrame(tick);
  });
}

document.documentElement.dataset.themeScript = "default_dark";

export {};
