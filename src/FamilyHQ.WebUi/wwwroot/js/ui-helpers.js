// Scrolls the Day view grid so the given minutes-of-day offset sits a quarter viewport from the
// top (the 24h grid is 1px per minute). Used by DayView's once-per-day scroll-to-now (FHQ-132).
export function scrollDayViewToMinutes(minutesOfDay) {
    const c = document.getElementById('day-view-container');
    if (c) {
        c.scrollTop = minutesOfDay - (c.clientHeight / 4);
    }
}

// Scrolls the currently-focused element to the centre of the viewport. Used by the event modal's
// numeric fields so the on-screen keyboard (which shrinks the viewport on focus) does not cover
// the active input. Deferred so the keyboard has begun resizing before we scroll.
export function scrollActiveElementIntoView() {
    const el = document.activeElement;
    if (el && typeof el.scrollIntoView === 'function') {
        setTimeout(() => el.scrollIntoView({ block: 'center', behavior: 'smooth' }), 150);
    }
}
