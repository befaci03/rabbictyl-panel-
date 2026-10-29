// User Avatars: adds deterministic geometric avatars to user rows. The seed is
// the username, so avatars stay stable without storing or uploading images.
const TARGET = "[data-avatar-user]";

function hash(value: string): number {
  let result = 2166136261;

  for (let index = 0; index < value.length; index += 1) {
    result ^= value.charCodeAt(index);
    result = Math.imul(result, 16777619);
  }

  return result >>> 0;
}

function avatarSvg(seed: number): string {
  const hue = seed % 360;
  const rotation = (seed % 4) * 90;
  const accent = `hsl(${hue} 65% 58%)`;

  return `<svg viewBox="0 0 24 24" width="26" height="26" aria-hidden="true">
    <rect width="24" height="24" rx="4" fill="${accent}" />
    <g transform="rotate(${rotation} 12 12)" fill="rgba(255,255,255,.86)">
      <rect x="5" y="5" width="5" height="5" rx="1" />
      <rect x="14" y="5" width="5" height="5" rx="1" />
      <rect x="5" y="14" width="5" height="5" rx="1" />
      <rect x="14" y="14" width="5" height="5" rx="1" />
    </g>
  </svg>`;
}

function renderAvatars(): void {
  document.querySelectorAll<HTMLElement>(TARGET).forEach((target) => {
    if (target.querySelector(".user-avatar")) {
      return;
    }

    const seed = hash(target.dataset.avatarUser ?? target.textContent ?? "user");
    const avatar = document.createElement("span");
    avatar.className = "user-avatar";
    avatar.innerHTML = avatarSvg(seed);
    target.prepend(avatar);
  });
}

renderAvatars();

export {};
