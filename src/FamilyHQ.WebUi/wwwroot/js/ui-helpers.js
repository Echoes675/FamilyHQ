// Scrolls the currently-focused element to the centre of the viewport. Used by the event modal's
// numeric fields so the on-screen keyboard (which shrinks the viewport on focus) does not cover
// the active input. Deferred so the keyboard has begun resizing before we scroll.
export function scrollActiveElementIntoView() {
    const el = document.activeElement;
    if (el && typeof el.scrollIntoView === 'function') {
        setTimeout(() => el.scrollIntoView({ block: 'center', behavior: 'smooth' }), 150);
    }
}
