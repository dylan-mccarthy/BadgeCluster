#!/usr/bin/env bash
# Run once in Azure Cloud Shell (Bash), or an Azure CLI authenticated terminal.
set -euo pipefail
subscription=''
location=''
resource_group=badgecluster
repository=dylan-mccarthy/BadgeCluster
output=artifacts/azure-bootstrap.json
usage() {
  printf 'Usage: bash scripts/bootstrap-azure.sh --subscription UUID --location REGION [--resource-group NAME] [--output PATH]\n'
}
while [[ $# -gt 0 ]]; do
  case $1 in
    --subscription|--location|--resource-group|--output)
      [[ $# -ge 2 ]] || { usage >&2; exit 1; }
      case $1 in
        --subscription) subscription=$2 ;;
        --location) location=$2 ;;
        --resource-group) resource_group=$2 ;;
        --output) output=$2 ;;
      esac
      shift 2 ;;
    --help|-h) usage; exit 0 ;;
    *) usage >&2; exit 1 ;;
  esac
done
[[ $subscription =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] || { printf 'Provide an explicit Azure subscription UUID.\n' >&2; exit 1; }
[[ $location =~ ^[a-z0-9]+$ ]] || { printf 'Provide an Azure region, for example eastus.\n' >&2; exit 1; }
[[ $resource_group =~ ^[a-zA-Z0-9_-]{1,90}$ ]] || { printf 'Invalid resource group name.\n' >&2; exit 1; }
command -v az >/dev/null || { printf 'Azure CLI is required; use Azure Cloud Shell (Bash).\n' >&2; exit 1; }
command -v python3 >/dev/null
# Do not initiate login or silently select a different subscription.
az account show --subscription "$subscription" --output none
# Register providers if needed; this needs subscription-level provider rights.
for provider in Microsoft.Web Microsoft.Storage Microsoft.ManagedIdentity; do
  state=$(az provider show --namespace "$provider" --subscription "$subscription" --query registrationState --output tsv)
  if [[ $state != Registered ]]; then
    az provider register --namespace "$provider" --subscription "$subscription" --wait --output none
  fi
done
az group create --subscription "$subscription" --name "$resource_group" --location "$location" \
  --tags project=BadgeCluster repository="$repository" --output none
identity_name=badgecluster-github
identity=$(az identity create --subscription "$subscription" --resource-group "$resource_group" --name "$identity_name" --output json)
client_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["clientId"])' <<< "$identity")
principal_id=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["principalId"])' <<< "$identity")
tenant_id=$(az account show --subscription "$subscription" --query tenantId --output tsv)
az identity federated-credential create --subscription "$subscription" --resource-group "$resource_group" \
  --identity-name "$identity_name" --name github-production \
  --issuer https://token.actions.githubusercontent.com \
  --subject "repo:$repository:environment:production" \
  --audiences api://AzureADTokenExchange --output none
scope="/subscriptions/$subscription/resourceGroups/$resource_group"
# Contributor deploys the app/storage. RBAC Administrator is required only
# because Bicep grants the web app its Table Data Contributor role. Both roles
# are confined to this resource group, never the subscription.
for role_id in b24988ac-6180-42a0-ab88-20f7382dd24c f58310d9-a9f6-439a-9e8d-f62e7b41a168; do
  existing=$(az role assignment list --subscription "$subscription" --scope "$scope" \
    --query "[?principalId=='$principal_id' && ends_with(roleDefinitionId, '/$role_id')].id" --output tsv)
  if [[ -z $existing ]]; then
    az role assignment create --subscription "$subscription" --assignee-object-id "$principal_id" \
      --assignee-principal-type ServicePrincipal --role "$role_id" --scope "$scope" --output none
  fi
done
python3 - "$output" "$client_id" "$tenant_id" "$subscription" "$resource_group" <<'PY'
import json, pathlib, sys
path = pathlib.Path(sys.argv[1])
path.parent.mkdir(parents=True, exist_ok=True)
settings = dict(zip(('AZURE_CLIENT_ID', 'AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_RESOURCE_GROUP'), sys.argv[2:]))
path.write_text(json.dumps(settings, indent=2) + '\n')
print('Azure OIDC bootstrap complete. No client secret was created.')
print('Add these variables to GitHub Settings > Environments > production:')
for key, value in settings.items():
    print(f'  {key}={value}')
print(f'Configuration saved to {path}. These IDs are not credentials.')
print(f'If GitHub CLI is authenticated, finish with: bash scripts/configure-github.sh {path}')
PY
