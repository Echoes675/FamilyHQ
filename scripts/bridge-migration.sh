#!/usr/bin/env bash
# Bridge migration: turn each historical spec in docs/superpowers/specs/
# into a Done ticket in the Obsidian vault.
# Run-once. Idempotent if rerun: skips if FHQ-N already exists for a slug.
set -euo pipefail

REPO_ROOT="${REPO_ROOT:-D:/Git/Echoes675/FamilyHQ}"
VAULT_ROOT="${VAULT_ROOT:-D:/Obsidian Vault/FamilyHQ}"
SPECS_DIR="$REPO_ROOT/docs/superpowers/specs"
PLANS_DIR="$REPO_ROOT/docs/superpowers/plans"

parse_spec_filename() {
  # Input: "2026-04-01-weather-integration-design.md"
  # Output: "2026-04-01|weather-integration"
  local fn="$1"
  local base="${fn%.md}"
  local date="${base:0:10}"
  local rest="${base:11}"        # strip date and dash
  rest="${rest%-design}"          # strip trailing -design
  echo "$date|$rest"
}

slug_to_title() {
  # "weather-integration" -> "Weather Integration"
  local slug="$1"
  echo "$slug" | awk -F- '{for(i=1;i<=NF;i++){$i=toupper(substr($i,1,1)) tolower(substr($i,2))}}1' OFS=' '
}

next_top_level_id() {
  # Scan vault for max FHQ-N folder across Tickets/, Done/, and Archive/
  # (a ticket may live in any one depending on its terminal/active state).
  # Skips dotted subtask folders.
  local max=0
  for top in Tickets Done Archive; do
    if [[ -d "$VAULT_ROOT/$top" ]]; then
      while IFS= read -r dir; do
        local name="${dir##*/}"
        if [[ "$name" =~ ^FHQ-([0-9]+)$ ]]; then
          local n="${BASH_REMATCH[1]}"
          if (( n > max )); then
            max=$n
          fi
        fi
      done < <(find "$VAULT_ROOT/$top" -maxdepth 1 -type d -name "FHQ-*")
    fi
  done
  echo $(( max + 1 ))
}

merge_date_for_file() {
  # Find the date the file was first added to the repo.
  git -C "$REPO_ROOT" log --diff-filter=A --follow --format=%ad --date=short -- "$1" | tail -1
}

write_ticket() {
  local id="$1" date="$2" slug="$3" spec_path="$4" plan_path="$5"
  local title; title="$(slug_to_title "$slug")"
  # Bridge-migrated tickets are born status:Done, so they live in Done/ not Tickets/.
  local folder="$VAULT_ROOT/Done/$id"
  local file="$folder/$id.md"

  mkdir -p "$folder"

  local plan_line="- Plan: (no plan file in repo)"
  if [[ -n "$plan_path" && -f "$REPO_ROOT/$plan_path" ]]; then
    plan_line="- Plan: \`$REPO_ROOT/$plan_path\`"
  fi

  cat > "$file" <<EOF
---
id: $id
type: Feature
status: Done
priority: P2
area: other
tags: [historical, bridge-migration]
created: $date
updated: $date
merged: $date
branch:
pr:
---

# $id — $title

Historical work imported via Bridge migration on $(date +%Y-%m-%d).

- Spec: \`$REPO_ROOT/$spec_path\`
$plan_line

(Vault and FamilyHQ repo are separate trees; paths above are absolute. Open in your editor / Obsidian's "Open in default app" right-click menu.)

This ticket was created retroactively so dashboards reflect a complete history of past work. The repo files remain the canonical design record.
EOF

  echo "wrote $file"
}

main() {
  if [[ ! -d "$SPECS_DIR" ]]; then
    echo "ERROR: $SPECS_DIR not found" >&2; exit 1
  fi

  local next; next=$(next_top_level_id)

  for spec in "$SPECS_DIR"/*.md; do
    [[ -e "$spec" ]] || { echo "no specs found"; exit 0; }
    local fn; fn=$(basename "$spec")
    local parsed; parsed=$(parse_spec_filename "$fn")
    local date="${parsed%|*}"
    local slug="${parsed#*|}"

    # Idempotency: skip if a bridge-migrated ticket already references this spec filename.
    # Search only top-level ticket files (FHQ-N/FHQ-N.md with purely numeric N) under
    # Tickets/, Done/, or Archive/ — and only when they carry the bridge-migration tag.
    # This avoids false positives from planning docs in FHQ-1 that list spec filenames
    # as examples in tables.
    local already_migrated=false
    for top in Tickets Done Archive; do
      [[ -d "$VAULT_ROOT/$top" ]] || continue
      while IFS= read -r dir; do
        local ticket_name; ticket_name="${dir##*/}"
        if [[ ! "$ticket_name" =~ ^FHQ-[0-9]+$ ]]; then
          continue
        fi
        local candidate="$dir/$ticket_name.md"
        if [[ -f "$candidate" ]] && grep -qF "bridge-migration" "$candidate" 2>/dev/null && grep -qF "$fn" "$candidate" 2>/dev/null; then
          already_migrated=true
          break 2
        fi
      done < <(find "$VAULT_ROOT/$top" -maxdepth 1 -type d -name "FHQ-*" 2>/dev/null)
    done
    if $already_migrated; then
      echo "skip $fn (already migrated)"
      continue
    fi

    # Try to find a matching plan: same date+slug, plan filename ends with the slug or "-plan.md"
    local plan_rel=""
    local plan_candidate1="$PLANS_DIR/$date-$slug.md"
    local plan_candidate2="$PLANS_DIR/$date-${slug}-plan.md"
    if [[ -f "$plan_candidate1" ]]; then
      plan_rel="docs/superpowers/plans/$date-$slug.md"
    elif [[ -f "$plan_candidate2" ]]; then
      plan_rel="docs/superpowers/plans/$date-${slug}-plan.md"
    fi

    local actual_date; actual_date=$(merge_date_for_file "docs/superpowers/specs/$fn")
    [[ -n "$actual_date" ]] || actual_date="$date"

    local id="FHQ-$next"
    write_ticket "$id" "$actual_date" "$slug" "docs/superpowers/specs/$fn" "$plan_rel"
    next=$(( next + 1 ))
  done
}

# Only run main if invoked directly (not when sourced for tests)
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
  main "$@"
fi
