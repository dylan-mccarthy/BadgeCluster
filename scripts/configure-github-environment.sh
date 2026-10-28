#!/usr/bin/env bash
set -euo pipefail
repository=dylan-mccarthy/BadgeCluster
command -v gh >/dev/null
error_file=$(mktemp)
trap 'rm -f "$error_file"' EXIT
# Leave existing reviewers/wait timers untouched by not rewriting an existing
# environment. Ask the administrator to adjust incompatible branch settings.
if policy=$(gh api "repos/$repository/environments/production" \
    --jq 'if .deployment_branch_policy.custom_branch_policies == true then "custom" else "other" end' 2>"$error_file"); then
  if [[ $policy != custom ]]; then
    printf 'Set production Deployment branches and tags to Selected branches and tags, then rerun. Existing environment protections were left untouched.\n' >&2
    exit 1
  fi
elif [[ $(<"$error_file") == *"HTTP 404"* ]]; then
  gh api --method PUT "repos/$repository/environments/production" --input - >/dev/null <<'JSON'
{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}
JSON
else
  cat "$error_file" >&2
  exit 1
fi
extra=$(gh api "repos/$repository/environments/production/deployment-branch-policies" \
  --jq '.branch_policies[] | select(.name != "main" or .type != "branch") | .name')
if [[ -n $extra ]]; then
  printf 'The production environment has additional branch/tag policies. Review and remove them to restrict deployment to main.\n' >&2
  exit 1
fi
existing=$(gh api "repos/$repository/environments/production/deployment-branch-policies" \
  --jq '.branch_policies[] | select(.name == "main" and .type == "branch") | .id')
if [[ -z $existing ]]; then
  gh api --method POST "repos/$repository/environments/production/deployment-branch-policies" \
    --input - >/dev/null <<'JSON'
{"name":"main","type":"branch"}
JSON
fi
printf 'GitHub production environment is restricted to main.\n'
