export function setWeatherOverlay(condition, isWindy) {
    const overlay = document.getElementById('weather-overlay');
    if (!overlay) return;

    // Clear existing classes
    overlay.className = '';

    if (!condition || condition === 'Clear') {
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
