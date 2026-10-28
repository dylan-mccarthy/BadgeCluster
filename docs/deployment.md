# Deploy BadgeCluster to Azure App Service

Open **Actions → Deploy to Azure → Run workflow** on `main`. Choose `preview`
for an infrastructure what-if, or `deploy` to test, provision, publish and
check readiness. No publish profile, storage key, client secret, GitHub
repository variable, or GitHub environment-administration permission is needed.

The deployment is in **Australia East**, resource group `badgecluster`.
The live dashboard is https://badgecluster-37nlkk2a4ucny.azurewebsites.net.
The initial Azure deployment and storage readiness checks have succeeded.

## How authentication works

A user-assigned Azure managed identity, `badgecluster-github`, trusts GitHub
OIDC tokens only for the exact subject:

```text
repo:dylan-mccarthy/BadgeCluster:ref:refs/heads/main
```

Issuer: `https://token.actions.githubusercontent.com`.
Audience: `api://AzureADTokenExchange`.
The federated credential is named `github-production`.

The workflow uses the `main` branch subject directly, without a GitHub
Environment. This was selected because the GitHub connection permits source
changes but rejects environment-administration writes with HTTP 403. Binding
Azure trust to the exact branch avoids relying on manually configured GitHub
environment branch policies. The build and Azure jobs also require `main`.
Only the Azure job receives `id-token: write`.

The four identifiers in [infra/deployment.json](../infra/deployment.json) are
**not credentials**: managed identity client ID, tenant ID, subscription ID,
and resource group. The workflow validates and loads them, then obtains
short-lived Azure credentials from GitHub OIDC. No password or token is stored
in the repository. There are no GitHub environment variables to enter manually.

The deployment identity has **Contributor** and **Role Based Access Control
Administrator**, confined to this resource group. The second role lets Bicep
grant the web app Table Data Contributor on its storage account. The app uses
its own system-managed identity for Table Storage. Do not share this resource
group with unrelated workloads.

## Deploy an update

1. Commit the change to `main`.
2. Open [Deploy to Azure](https://github.com/dylan-mccarthy/BadgeCluster/actions/workflows/deploy.yml).
3. Select **Run workflow**, branch **main**, operation **preview** if you want
   a what-if first, then run **deploy**.
4. Open the dashboard link in the successful job summary.

The workflow tests the API against Azurite and compiles Bicep. It packages the
exact tested revision, applies infrastructure in Incremental mode, deploys the
API/dashboard, and checks `/health/ready`. Production runs are serialized and
are never canceled by a newer run. Accepted totals remain in Azure Table Storage
outside the deployment package. The first B1 setup has no staging-slot swap,
so allow a brief restart during deployment.

The App Service tier defaults to `B1`; `S1` and `P1v3` are also supported by
Bicep. B1 incurs an ongoing App Service charge; storage has usage charges.
The optional repository variable `AZURE_APP_SERVICE_SKU` can override B1 if
configured later; it is not required. Confirm Australia East pricing before
changing the tier.

## Re-create the one-time Azure setup

This is already done for the current deployment. For recovery or another
subscription, use Azure Cloud Shell (Bash) or an authenticated Azure CLI terminal:

```sh
git clone https://github.com/dylan-mccarthy/BadgeCluster.git
cd BadgeCluster
bash scripts/bootstrap-azure.sh \
  --subscription '<your-subscription-id>' \
  --location australiaeast
```

The bootstrap registers providers if needed, creates the resource group and
managed identity, sets the exact branch-bound federated credential, and grants
the two roles at resource-group scope. The caller needs resource creation,
provider-registration and role-assignment permissions, for example Owner on
the selected subscription. No Entra application-registration rights are needed.

It writes `infra/deployment.json`; review and commit that nonsecret file to
`main`. Re-running reuses the identity and existing role assignments. Pass
`--resource-group another-name` for a dedicated alternative. The bootstrap does
not create the paid App Service plan; the **deploy** operation does.

## Troubleshooting and operation

- **OIDC subject mismatch:** run on `main`; ensure there is no workflow
  `environment:` declaration, since that changes the OIDC subject. Verify the
  exact repository spelling and federated subject above.
- **Invalid/missing configuration:** check all four identifiers in
  `infra/deployment.json`. The loader rejects unexpected keys, invalid IDs,
  and newline injection before writing GitHub runner outputs.
- **AuthorizationFailed:** deployment identity needs both group-scoped roles;
  the web app identity needs Table Data Contributor on the storage account.
- **First-deploy readiness failure:** role assignments can take minutes to
  propagate. Readiness retries are bounded. Inspect App Service startup logs,
  wait for propagation and rerun; do not add storage keys as a workaround.
- **Unavailable runtime/region/SKU:** confirm .NET 10 Linux App Service support,
  provider registration, subscription policy and region quota.
- **Rollback:** revert the unwanted commit on `main` and run deployment.
  Reverting infrastructure does not reverse stored contribution data.
- **Stop charges:** deleting the resource group removes the app, plan, storage
  (including accepted contributions), and deployment identity. Deletion is
  destructive and intentionally not automated by the deployment workflow.
