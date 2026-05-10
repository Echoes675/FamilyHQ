#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/bridge-migration.sh"

# Test parse_spec_filename
result=$(parse_spec_filename "2026-04-01-weather-integration-design.md")
expected="2026-04-01|weather-integration"
[[ "$result" == "$expected" ]] || { echo "FAIL: parse_spec_filename: got '$result' expected '$expected'"; exit 1; }

# Test slug_to_title
result=$(slug_to_title "weather-integration")
expected="Weather Integration"
[[ "$result" == "$expected" ]] || { echo "FAIL: slug_to_title: got '$result' expected '$expected'"; exit 1; }

# Test parse_spec_filename with multi-word slug
result=$(parse_spec_filename "2026-03-29-ui-redesign-time-of-day-theming-design.md")
expected="2026-03-29|ui-redesign-time-of-day-theming"
[[ "$result" == "$expected" ]] || { echo "FAIL: complex slug: got '$result' expected '$expected'"; exit 1; }

echo "All tests passed."
