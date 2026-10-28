# Deploy BadgeCluster to Azure App Service

Use **Actions → Deploy to Azure → Run workflow** on `main`. Choose `preview`
for an infrastructure what-if, or `deploy` to test, provision, publish and
check readiness. No publish profile, storage key, or client secret is needed.

## One-time account setup

The repository contains everything needed for deployment, but Azure identity
creation and GitHub environment settings require an administrator's signed-in
session. Azure resource IDs alone do not grant access. This workspace's GitHub
integration cannot administer environments/variables (HTTP 403), and it has
no configured Azure identity, so those account-side steps cannot be performed
by the repository-writing integration.

### 1. Run the Azure bootstrap

Open [Azure Cloud Shell](https://shell.azure.com/) in **Bash**, or use an
already authenticated Azure CLI terminal. Clone this repository and run:

```sh
git clone https://github.com/dylan-mccarthy/BadgeCluster.git
cd BadgeCluster
bash scripts/bootstrap-azure.sh \
  --subscription '<your-subscription-id>' \
  --location australiaeast
```

The selected deployment region is **Australia East**. Pass the subscription
ID explicitly. The default resource group is `badgecluster`; use
`--resource-group another-name` for a dedicated alternative. Do not share
this resource group with unrelated workloads: the deployment identity can
manage resources and role assignments within it.

The bootstrap registers Azure providers when needed, creates the resource
group, creates a user-assigned managed identity named `badgecluster-github`,
and adds one GitHub federated credential:

- Issuer: `https://token.actions.githubusercontent.com`
- Subject: `repo:dylan-mccarthy/BadgeCluster:environment:production`
- Audience: `api://AzureADTokenExchange`

The identity receives **Contributor** and **Role Based Access Control
Administrator** on this resource group only. The second role is required
because Bicep grants the deployed web app Table Data Contributor access.
The bootstrap caller therefore needs resource creation/provider-registration
rights and role-assignment rights (for example Owner on the selected
subscription). No Microsoft Entra application-registration permission is
needed. The federated identity and its two role assignments can be reused
on subsequent runs. Do not delete them as part of app redeployment.

The script writes `artifacts/azure-bootstrap.json` and prints four nonsecret
settings. No App Service plan is created until you run **deploy**. Bootstrap
resources (resource group and identity) do not provision the paid B1 plan.

### 2. Configure GitHub production

Open [the repository environment settings](https://github.com/dylan-mccarthy/BadgeCluster/settings/environments).
Create an environment named exactly **production**.

Under **Deployment branches and tags**, choose **Selected branches and tags**
and add a **Branch** rule for `main` only. Do not add other branches or tags.
This restriction is part of the OIDC trust boundary, since its Azure subject
names the environment rather than a branch.

Add these **environment variables**, using the bootstrap output:

| Variable | Source |
| --- | --- |
| `AZURE_CLIENT_ID` | Managed identity client ID |
| `AZURE_TENANT_ID` | Subscription's tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Selected subscription ID |
| `AZURE_RESOURCE_GROUP` | `badgecluster`, unless overridden |

These are **Variables**, not Secrets. No secret or publish profile is required.
`AZURE_APP_SERVICE_SKU` is optional and defaults to `B1`; supported values are
`B1`, `S1`, and `P1v3`. B1 incurs an ongoing App Service charge after deployment;
Table Storage also has usage charges. Confirm pricing for Australia East.

Alternatively, with GitHub CLI signed in as a repository administrator, run:

```sh
bash scripts/configure-github.sh artifacts/azure-bootstrap.json
```

This creates the environment, limits it to `main`, and sets the four variables.
It fails rather than deleting pre-existing additional branch/tag rules. If
`gh` isn't installed or authenticated in Cloud Shell, use the GitHub settings
UI above. Do not paste passwords, tokens or storage credentials into variables.

### 3. Preview, then deploy

Open [Deploy to Azure](https://github.com/dylan-mccarthy/BadgeCluster/actions/workflows/deploy.yml),
select **Run workflow**, keep branch **main**, and select **preview**. Review
the resource changes in the workflow log. Then run again with **deploy**.

The workflow tests the API against Azurite and compiles Bicep before logging
in to Azure. For deployments, it packages the exact tested revision, applies
infrastructure in Incremental mode, deploys the API/dashboard, and checks
`/health/ready`. Concurrent production runs are serialized; new runs do not
cancel an in-progress deployment. Only the Azure job receives `id-token: write`.

The app URL appears in the GitHub job summary and production deployment.
It is generated from the resource group, so no globally unique app name needs
to be entered manually. A normal app update does not recreate accepted totals.
Azure Table Storage holds counters separately from the App Service package.

## Troubleshooting and operation

- **Missing production variable:** configure all four variables in Step 2.
- **OIDC subject mismatch:** environment must be `production`; check the exact
  repository spelling and federated credential subject above.
- **AuthorizationFailed:** bootstrap identity needs both group-scoped roles;
  the web app identity needs Table Data Contributor on the storage account.
- **First-deploy storage/readiness failure:** managed identity role assignments
  can take several minutes to propagate. The workflow checks readiness up to
  24 times with bounded connection/read timeouts. Inspect App Service logs,
  wait for propagation and rerun deployment; do not add storage keys as a fix.
- **Unavailable runtime/region/SKU or provider:** confirm .NET 10 Linux App
  Service and the selected pricing tier in the chosen subscription/region,
  provider registration, policy restrictions and quota.
- **Rollback:** use `git revert` on the unwanted application change, then run
  deployment from `main`. Reverting infrastructure does not automatically
  reverse changes to stored contribution data. This first B1 setup has no
  staging-slot swap; expect a brief restart during deployment.
- **Stop charges:** deleting the resource group removes the app, plan, storage
  (including accepted contributions), and deployment identity. Deletion is
  destructive and is intentionally not automated by the deployment workflow.

The initial local release build, API tests and Bicep compilation are recorded
in `docs/validation.md`. A live Azure deployment still needs the one-time
account setup and a successful workflow run; publishing YAML alone is not
proof that Azure resources exist.
