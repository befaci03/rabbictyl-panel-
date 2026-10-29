// Default Light: gently reveal cards as they enter the viewport.
const cards = document.querySelectorAll<HTMLElement>(".card, .stat");
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

if (!reduceMotion && "IntersectionObserver" in window) {
  const observer = new IntersectionObserver(
    (entries) => {
      for (const entry of entries) {
        if (!entry.isIntersecting) {
          continue;
        }

        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      }
    },
    { threshold: 0.15 },
  );

  cards.forEach((card) => {
    card.classList.add("reveal");
    observer.observe(card);
  });
} else {
  cards.forEach((card) => card.classList.add("is-visible"));
}

document.documentElement.dataset.themeScript = "default_light";

export {};
