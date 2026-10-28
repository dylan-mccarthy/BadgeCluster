# Validation from the initial cloud build

- Release build and `dotnet publish` completed without warnings or errors.
- Sixteen integration tests passed against Azurite Table Storage: null pi at
  zero, health/dashboard delivery, duplicates/old requests, concurrent duplicate
  and distinct-node submissions, multi-session active-node uniqueness, expiry,
  malformed inputs, oversized bodies, monotonic counters, impossible deltas,
  restart persistence and rate limiting.
- `bicep build infra/main.bicep` compiled successfully with no diagnostics.
  No Azure deployment or `what-if` has been performed against a subscription.
- Headless Chromium exercised empty/live dashboard states, computed pi,
  390px mobile width, stale cache after HTTP 503, and no JavaScript errors.
  Test data was generated locally; it did not come from real badges.
- Badge worker/protocol tests and app validators ran in the separate badge
  repository. End-to-end badge HTTPS and battery checks remain outstanding.

For the optional browser smoke test, install Python Playwright and Chromium,
start an isolated local coordinator on port 5080, then run
`python tests/check_dashboard.py`. It adds one million synthetic samples to a
fresh random session and must only be run against local test storage. The
script writes desktop/mobile previews to `artifacts/`, which is Git-ignored.

CI validates the API against Azurite, builds the publish artifact and compiles
Bicep. Azure managed identity, role propagation, App Service runtime support
and event Wi-Fi/proxy behavior must still be tested in the chosen subscription.

## GitHub OIDC deployment automation

- `actionlint` passes CI and the manual deployment workflow; Bash scripts pass
  syntax checks. Twelve script/configuration tests pass, exercising exact
  branch-bound OIDC trust, group-scoped roles, reruns, identifier validation,
  runner output safety and bounded readiness retries with fake external CLIs.
- GitHub returned HTTP 403 on environment-administration writes even after
  CLI login. The workflow therefore uses the exact `main` branch OIDC subject
  and checked-in nonsecret identifiers, without requiring GitHub environments.
- Azure CLI device login succeeded. Resource group `badgecluster`, managed
  identity, federated credential and scoped roles were created in Australia East.
- Azure deployment what-if and provider validation succeeded. The B1 App Service,
  storage account, table and app storage role were provisioned. The published
  Release package deployed successfully.
- Live HTTPS checks pass: `/health/ready` returns `ready`, the dashboard returns
  its expected content, and `/api/v1/cluster` starts with zero samples/null pi.
  No synthetic samples were added to production storage.
- The first GitHub OIDC deployment run is the remaining verification for this
  branch-bound workflow; live badge networking and battery checks remain separate.
