#!/bin/sh
set -e

CONFIG_FILE="/usr/share/nginx/html/appsettings.json"

if [ -n "$BACKEND_URL" ]; then
    sed -i "s|https://localhost:7196|${BACKEND_URL}|g" "$CONFIG_FILE"
fi

exec nginx -g "daemon off;"
