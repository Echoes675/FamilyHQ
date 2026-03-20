#!/bin/sh
set -e

CONFIG_FILE="/usr/share/nginx/html/appsettings.json"
HEALTH_FILE="/usr/share/nginx/html/health"

if [ -n "$BACKEND_URL" ]; then
    sed -i "s|https://localhost:7196|${BACKEND_URL}|g" "$CONFIG_FILE"
fi

printf '{"status":"healthy","service":"webui","startedAt":"%s"}' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$HEALTH_FILE"
chmod 644 "$HEALTH_FILE"

exec nginx -g "daemon off;"
