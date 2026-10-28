# BadgeCluster coordinator

[GitHub repository](https://github.com/dylan-mccarthy/BadgeCluster)

Hundreds of processors. One irrational goal.

ASP.NET Core (.NET 10 LTS) API and read-only dashboard for the Universe 2026
BadgeCluster experiment. Deploys to **Azure App Service**, with persistent
Azure Table Storage and managed-identity access. Badge code lives separately
in [dylan-mccarthy/badger](https://github.com/dylan-mccarthy/badger), under
`badge/apps/badgecluster/`.

## Deploy from GitHub

Use [Deploy to Azure](https://github.com/dylan-mccarthy/BadgeCluster/actions/workflows/deploy.yml)
on `main` after the [one-time OIDC setup](docs/deployment.md).
The workflow tests, previews or provisions infrastructure, deploys the API and
dashboard, and checks storage readiness. The selected region is Australia East.
No Azure password, publish profile or storage key is stored in GitHub.

## Run locally

Install .NET 10 SDK and Azurite (`npm install -g azurite`). In one terminal:

```sh
azurite --location .azurite --skipApiVersionCheck
```

In another:

```sh
Storage__ConnectionString='UseDevelopmentStorage=true' dotnet run --project src/BadgeCluster.Api --urls http://localhost:5080
```

Open http://localhost:5080 for the dashboard. The local emulator uses its
well-known development credential; production uses managed identity.

```sh
dotnet test tests/BadgeCluster.Api.Tests
dotnet publish src/BadgeCluster.Api -c Release -o artifacts/publish
```

Tests require Azurite running on its default ports, or
`TEST_STORAGE_CONNECTION` pointing to an isolated test account. Tests create
and delete uniquely named tables. CI runs the real Table Storage emulator.

## API

- `POST /api/v1/contributions`: cumulative counters scoped to `(sessionId,nodeId)`.
- `GET /api/v1/cluster`: accepted samples, inside count, approximate pi, active
  distinct nodes, and last accepted update timestamp. Zero samples means null pi.
- `GET /health`: liveness; `GET /health/ready`: storage readiness.

```sh
curl http://localhost:5080/api/v1/contributions \
  -H 'Content-Type: application/json' \
  -d '{"protocolVersion":1,"nodeId":"node-example","sessionId":"session-example","sequence":1,"samples":250,"inside":196}'
```

IDs allow 1–64 ASCII letters, digits, underscores or hyphens. Real clients
must generate collision-resistant random IDs; examples are illustrative.
No hardware IDs, attendee details or Wi-Fi credentials are required.

Each accepted update atomically writes a session, the global aggregate and a
node activity row in one Table Storage partition transaction. ETags protect
all writes. Conflicts reload state and retry with bounded jitter. Duplicates
or older sequences return the current acknowledgement without adding totals
or refreshing activity. New sequences with unchanged counters are heartbeats.
Active means a distinct node with a new accepted sequence in the last five
minutes. Aggregate fields are one consistent entity; active membership is
queried separately and can change while the response is assembled.

Validation rejects decreasing counters and impossible inside-count deltas.
Limits: 2 KiB body; 1 trillion samples/session; 1 million samples/update;
1 billion sequence values; 100,000 retained sessions per deployment;
9 quadrillion global samples (to keep dashboard integer counters exact). At most
100 requests/second per process and 120 requests/minute per observed client IP,
with no queue. These initial quotas are code constants. 400 means malformed
input, 409 means counter/quota conflict, 429 means rate limited, and 503 means
storage unavailable or timeout. Clients should back off on 429/503.

## Manual Azure deployment alternative

Review the Bicep template and costs first. It creates a paid Linux App Service
plan (B1 default), a .NET 10 web app with HTTPS only, a StorageV2 account and
Table Data Contributor assignment for the app's system-managed identity.
No shared storage key is configured. Run from a signed-in Azure CLI with
resource and role-assignment permissions, choosing your subscription explicitly.

```sh
az account set --subscription '<subscription-id>'
az group create --name badgecluster --location australiaeast
az deployment group what-if --resource-group badgecluster --template-file infra/main.bicep
az deployment group create --resource-group badgecluster --template-file infra/main.bicep
```

Take `deployedAppName` from the deployment outputs, then publish a ZIP with the
**contents** of `artifacts/publish` at its root:

```sh
(cd artifacts/publish && zip -r ../badgecluster.zip .)
az webapp deploy --resource-group badgecluster --name '<deployedAppName>' --src-path artifacts/badgecluster.zip --type zip
```

Managed identity role assignments can take several minutes to propagate; wait
and restart the app if startup fails during propagation. Verify `/health/ready`,
submit a sample, restart App Service, then confirm the sample remains counted.
The infrastructure has been authored for deployment; creating cloud resources
is a separate step. This workspace does not contain a production deployment.

## Operational limits and remaining work

- All contributions use one partition and one totals entity for correct atomic
  aggregation. This is suitable for infrequent badge updates, not unlimited
  throughput. Load-test with realistic batches before a large event.
- Active-node queries scan the node-row range; retained sessions/nodes are
  bounded by quota. No automated retention/deletion is implemented. Deleting
  session rows alone would break idempotency, so define a coordinated event
  retention policy before purging anything. Cumulative aggregates persist.
- Rate limits are per process. App Service may expose a shared proxy/NAT IP;
  test the actual forwarded-address behavior and event Wi-Fi. Do not blindly
  trust caller-provided `X-Forwarded-For`. For public rollout, put Azure API
  Management/Front Door in front with an aggregate quota and configure trusted
  proxy handling for your topology. Unauthenticated counters cannot prove
  honest computation. No prizes, rankings or anti-cheat claims.
- Application logging avoids request bodies; Azure access logs may contain
  normal connection metadata. Review logging/retention before inviting users.
- Dashboard polls every 15 seconds, keeps cached numbers visibly stale on
  errors, has no third-party assets, and labels the sphere as illustrative.
- Badge networking remains gated on a physical firmware/TLS/battery spike;
  the offline badge prototype does not yet send requests to this API.

The supplied baseline is in [docs/specification.md](docs/specification.md).
User instruction supersedes its Container Apps example with App Service and
splits the badge and coordinator into separate repositories.
