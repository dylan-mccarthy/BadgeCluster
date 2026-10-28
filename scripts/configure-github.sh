#!/usr/bin/env bash
# Uses the caller's existing GitHub CLI login; never creates a PAT or secret.
set -euo pipefail
config=${1:-artifacts/azure-bootstrap.json}
repository=dylan-mccarthy/BadgeCluster
[[ -f $config ]] || { printf 'Missing bootstrap JSON. Run bootstrap-azure.sh first.\n' >&2; exit 1; }
command -v gh >/dev/null || { printf 'GitHub CLI is required. Alternatively enter the four variables in the production environment settings.\n' >&2; exit 1; }
command -v python3 >/dev/null
gh auth status >/dev/null
python3 - "$config" <<'PY'
import json, re, sys
settings = json.load(open(sys.argv[1]))
keys = {'AZURE_CLIENT_ID', 'AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_RESOURCE_GROUP'}
if set(settings) != keys or any(not isinstance(v, str) or not v for v in settings.values()):
    raise SystemExit('Expected exactly four nonempty Azure bootstrap settings')
for key in keys - {'AZURE_RESOURCE_GROUP'}:
    if not re.fullmatch(r'[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}', settings[key]):
        raise SystemExit('Invalid Azure ID: ' + key)
if not re.fullmatch(r'[a-zA-Z0-9_-]{1,90}', settings['AZURE_RESOURCE_GROUP']):
    raise SystemExit('Invalid resource group name')
PY
bash scripts/configure-github-environment.sh
for setting in AZURE_CLIENT_ID AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID AZURE_RESOURCE_GROUP; do
  value=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' "$config" "$setting")
  gh variable set "$setting" --repo "$repository" --env production --body "$value"
done
printf 'GitHub production variables configured. Open https://github.com/%s/actions/workflows/deploy.yml and select Run workflow on main.\n' "$repository"
