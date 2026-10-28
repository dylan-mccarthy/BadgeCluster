# Resume coordinator development

## Current state

This repository's `main` contains the .NET 10 API, dashboard, Table Storage
persistence, tests, Bicep, and working manual GitHub OIDC deployment.
The live app is https://badgecluster-37nlkk2a4ucny.azurewebsites.net in
Australia East (B1 plan, resource group `badgecluster`).

The separate [badge repository](https://github.com/dylan-mccarthy/badger/tree/feature/badgecluster)
has the app and host test harness on `feature/badgecluster`, proposed in a
draft PR pending review. Its
[handoff](https://github.com/dylan-mccarthy/badger/blob/feature/badgecluster/docs/badgecluster/development.md)
and [live test evidence](https://github.com/dylan-mccarthy/badger/blob/feature/badgecluster/docs/badgecluster/azure-testing.md)
are the starting point for badge work.

## Local development

Install .NET 10 SDK, Node/npm and Azurite. Clone this repo and run:

```sh
npm install -g azurite
azurite --location .azurite --skipApiVersionCheck
```

Keep the emulator running. In a second terminal at the repo root:

```sh
Storage__ConnectionString='UseDevelopmentStorage=true' dotnet run --project src/BadgeCluster.Api --urls http://localhost:5080
dotnet test tests/BadgeCluster.Api.Tests -c Release
python3 -m unittest discover -s tests -p 'test_deployment_scripts.py'
dotnet publish src/BadgeCluster.Api -c Release -o artifacts/publish
```

Open http://localhost:5080. There are 16 API integration tests (real Azurite)
and 12 bootstrap/configuration tests (fake external CLIs). Tests create/delete
isolated tables; never point `TEST_STORAGE_CONNECTION` at production data.
The optional `python tests/check_dashboard.py` test requires Python Playwright
and its Chromium install. It targets the local server and adds synthetic
samples to local storage only.

## Deployment

Use **Actions → Deploy to Azure → Run workflow → main → preview/deploy**.
The [deployment guide](deployment.md) covers setup, rollback and diagnostics.
The exact immutable-ID GitHub OIDC subject is recorded there; don't substitute
a legacy subject or add a workflow `environment:` declaration without updating
Azure trust. `infra/deployment.json` contains nonsecret identifiers; no GitHub
secrets, variables or environment-administration step is needed.

The full [deployment verification](https://github.com/dylan-mccarthy/BadgeCluster/actions/runs/40728894117)
passed. No runtime credentials or CLI caches are committed. An Azure login is
needed to change Azure identity/RBAC/bootstrap configuration; routine deployment
uses GitHub OIDC. A new workspace does not need the previous session's CLI login
to run local tests or dispatch the existing deployment workflow.

## Remaining work

The actual badge app has passed a live host-to-Azure test with 250 real samples
and a response-loss retry that did not double-count. That host test contributed
250 samples to shared totals. Physical-badge Wi-Fi/TLS, calibrated battery
readings, orientation, renderer timing and transport cleanup are not verified.
The badge's device transport remains offline by default.

Coordinate the next transport work in the badge repo, then test 2–5 actual
badges. Before a large rollout, measure battery impact and event-network/rate
limit behavior, load-test Table Storage contention, and define a coordinated
retention policy that preserves idempotency. See [validation.md](validation.md)
for completed checks and [specification.md](specification.md) for the baseline.
