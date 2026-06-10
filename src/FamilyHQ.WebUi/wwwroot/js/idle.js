// src/FamilyHQ.WebUi/wwwroot/js/idle.js
//
// FHQ-63: tracks the time of the last user interaction so the dashboard can detect an idle
// kiosk and auto-advance to the current day. Uses performance.now() — a MONOTONIC clock that
// is unaffected by wall-clock / timezone / date changes, which is exactly the thing we are
// detecting. The display date itself comes from the .NET clock, never from here.

let lastInteraction = performance.now();

function stamp() {
    lastInteraction = performance.now();
}

// Passive, capture-phase document listeners so a child handler that calls stopPropagation
// still refreshes the stamp. pointerdown covers touch (including the start of a scroll) and
// mouse; keydown covers the rare on-screen-keyboard / hardware-keyboard interaction.
export function init() {
    document.addEventListener('pointerdown', stamp, { passive: true, capture: true });
    document.addEventListener('keydown', stamp, { capture: true });
}

export function getIdleMs() {
    return performance.now() - lastInteraction;
}

export function markInteraction() {
    stamp();
}

// --- dev / E2E hooks (only reachable when the clock override feature flag is on) ---

// Pretend the last interaction was `ms` milliseconds ago, to drive the idle path
// deterministically in E2E without waiting. Harmless if ever called in production.
export function setIdleForTest(ms) {
    lastInteraction = performance.now() - ms;
}

// Exposes a window bridge so Playwright can advance the clock and force an immediate idle
// check, avoiding a dependency on the 30s poll cadence. Attached only by Index when the
// override flag is enabled.
export function attachDevBridge(dotNetRef) {
    window.familyHqKiosk = {
        advanceDays: (n) => dotNetRef.invokeMethodAsync('DevAdvanceClockDays', n),
        runIdleCheck: () => dotNetRef.invokeMethodAsync('DevRunIdleCheck'),
        setIdle: (ms) => setIdleForTest(ms)
    };
}
