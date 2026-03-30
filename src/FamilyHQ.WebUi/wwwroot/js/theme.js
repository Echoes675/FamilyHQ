export function setTheme(period) {
    document.body.setAttribute('data-theme', period.toLowerCase());
}

export function setDisplayProperty(name, value) {
    document.body.style.setProperty(name, value);
}

export function removeDisplayProperty(name) {
    document.body.style.removeProperty(name);
}
