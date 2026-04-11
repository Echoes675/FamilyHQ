#!/bin/sh
set -e

CONFIG_FILE="/usr/share/nginx/html/appsettings.json"
HEALTH_FILE="/usr/share/nginx/html/health"

if [ -n "$BACKEND_URL" ]; then
    sed -i "s|https://localhost:7196|${BACKEND_URL}|g" "$CONFIG_FILE"
fi

# Dev/staging only: flip the weather override feature flag on.
# Only an explicit "true" flips it; any other value leaves the default (false),
# so preprod and production are safe even if the env var is missing.
if [ "$FEATURE_WEATHER_OVERRIDE_ENABLED" = "true" ]; then
    sed -i 's|"FeatureWeatherOverride": false|"FeatureWeatherOverride": true|' "$CONFIG_FILE"
fi

printf '{"status":"healthy","service":"webui","startedAt":"%s"}' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HEALTH_FILE"
chmod 644 "$HEALTH_FILE"

exec nginx -g "daemon off;"
