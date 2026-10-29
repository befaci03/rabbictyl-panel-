import { connectRealtime } from "./realtime";

const authenticated = document.body.dataset.authenticated === "true";
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

document.documentElement.classList.toggle("motion-ok", !reduceMotion);

if (authenticated) {
  void connectRealtime().catch((error: unknown) => {
    // Realtime is an enhancement; never break the page when it is unavailable.
    console.warn("[realtime] could not connect", error);
  });
}
