#!/usr/bin/env bash
set -euo pipefail
app_url=${1:?Usage: check-readiness.sh https://your-app.azurewebsites.net}
if [[ ! $app_url =~ ^https://[a-zA-Z0-9-]+\.azurewebsites\.net$ ]]; then
  printf 'Expected an HTTPS Azure App Service URL.\n' >&2
  exit 1
fi
# Allow time for initial managed-identity role propagation. Fail visibly if
# it never becomes ready; a default App Service page is not a successful check.
for attempt in {1..24}; do
  if response=$(curl --fail --silent --show-error --connect-timeout 5 --max-time 10 "$app_url/health/ready") &&
      jq -e '.status == "ready"' >/dev/null <<< "$response"; then
    printf 'BadgeCluster is ready at %s\n' "$app_url"
    exit 0
  fi
  if [[ $attempt -lt 24 ]]; then sleep 10; fi
done
printf 'Readiness failed. Inspect App Service startup logs and the Table Storage role assignment; see docs/deployment.md.\n' >&2
exit 1
