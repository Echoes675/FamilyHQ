export function setTheme(period) {
    document.body.setAttribute('data-theme', period.toLowerCase());
}

export function setDisplayProperty(name, value) {
    document.body.style.setProperty(name, value);
}

export function removeDisplayProperty(name) {
    document.body.style.removeProperty(name);
}

// FHQ-178: the kiosk's OWN operating-system timezone. This is the one automatic source that
// describes the family rather than the server's datacentre — a server-side IP lookup geolocates the
// host, and always will. No network call and nothing leaves the device.
export function getKioskTimeZone() {
    try {
        return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;
    } catch {
        return null;
    }
}
