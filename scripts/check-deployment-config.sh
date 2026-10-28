#!/usr/bin/env bash
set -euo pipefail

if [[ ${GITHUB_REF:-} != refs/heads/main ]]; then
  printf 'Deployments must run from main.\n' >&2
  exit 1
fi
for setting in AZURE_CLIENT_ID AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID AZURE_RESOURCE_GROUP; do
  if [[ -z ${!setting:-} ]]; then
    printf 'Missing deployment setting: %s. See infra/deployment.json and docs/deployment.md.\n' "$setting" >&2
    exit 1
  fi
done
for setting in AZURE_CLIENT_ID AZURE_TENANT_ID AZURE_SUBSCRIPTION_ID; do
  if [[ ! ${!setting} =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    printf 'Invalid Azure ID in deployment setting: %s.\n' "$setting" >&2
    exit 1
  fi
done
if [[ ! $AZURE_RESOURCE_GROUP =~ ^[a-zA-Z0-9_-]{1,90}$ ]]; then
  printf 'Use a resource group name containing only letters, digits, underscores or hyphens.\n' >&2
  exit 1
fi
case ${APP_SERVICE_SKU:-B1} in
  B1|S1|P1v3) ;;
  *) printf 'Unsupported App Service pricing tier. Use B1, S1 or P1v3.\n' >&2; exit 1 ;;
esac
