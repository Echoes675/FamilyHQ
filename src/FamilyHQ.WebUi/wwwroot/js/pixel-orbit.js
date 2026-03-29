// src/FamilyHQ.WebUi/wwwroot/js/pixel-orbit.js
const ORBIT_PATH = [
    [  0,  0 ],
    [  2,  1 ],
    [  3,  0 ],
    [  2, -1 ],
    [  0, -2 ],
    [ -2, -1 ],
    [ -3,  0 ],
    [ -2,  1 ],
];

const BASE_INTERVAL_MS = 120_000;
const JITTER_MS = 15_000;

function nextInterval() {
    return BASE_INTERVAL_MS + (Math.random() * 2 - 1) * JITTER_MS;
}

export function init(elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;

    el.style.transition = 'transform 2s ease-in-out';

    let step = 0;

    function advance() {
        step = (step + 1) % ORBIT_PATH.length;
        const [x, y] = ORBIT_PATH[step];
        el.style.transform = `translate(${x}px, ${y}px)`;
        setTimeout(advance, nextInterval());
    }

    setTimeout(advance, nextInterval());
}
