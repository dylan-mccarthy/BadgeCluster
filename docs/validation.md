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

- `actionlint` 1.7.7 passes for CI and the manual deployment workflow.
- All shell scripts pass Bash syntax checks.
- Twelve deployment-script tests run the real shell scripts with recorded fake
  Azure/GitHub/curl CLIs, checking environment variables, exact OIDC subject,
  resource-group-scoped roles, reruns, missing permissions, branch policies,
  input validation and bounded readiness checks. These are automated script
  tests, not proof of a successful Azure login or deployment.
- GitHub's integration returned HTTP 403 for environments and Actions
  variables. No production environment/variables were created by this session.
- No Azure identity is configured in this workspace. The supplied subscription
  and Australia East selection require `bootstrap-azure.sh` under an
  authenticated Azure session, then GitHub production configuration.
