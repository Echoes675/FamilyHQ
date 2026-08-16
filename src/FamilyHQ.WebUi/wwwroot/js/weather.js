export function setWeatherOverlay(condition, isWindy) {
    const overlay = document.getElementById('weather-overlay');
    if (!overlay) return;

    // Clear existing classes
    overlay.className = '';

    // FHQ-115: 'Unknown' means we could not identify the weather, so show no animation
    // rather than guessing at one.
    if (!condition || condition === 'Clear' || condition === 'Unknown') {
        overlay.innerHTML = '';
        return;
    }

    overlay.className = `weather-${condition.toLowerCase()}`;
    if (isWindy) {
        overlay.classList.add('weather-windy');
    }
}

export function clearWeatherOverlay() {
    const overlay = document.getElementById('weather-overlay');
    if (!overlay) return;
    overlay.className = '';
    overlay.innerHTML = '';
}
