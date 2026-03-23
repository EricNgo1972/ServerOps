# ServerOps

ServerOps is a `.NET 8` Blazor Server platform for managing deployed applications and their host services across Linux and Windows.

It provides:

- service discovery and service control
- port discovery and service-to-port correlation
- cloudflared tunnel detection and exposure
- GitHub release listing
- staged deployment with verification and rollback
- deployment history and log viewing
- one-click deploy with optional automatic hostname generation

## Solution Structure

- `ServerOps.Domain`: domain records and enums
- `ServerOps.Application`: abstractions, DTOs, and orchestration logic
- `ServerOps.Infrastructure`: OS integrations, storage, deployment, Cloudflare, GitHub
- `ServerOps.Web`: Blazor Server UI and minimal deploy API
- `ServerOps.Application.Tests`: application-level tests
- `ServerOps.Infrastructure.Tests`: infrastructure and orchestration tests
- `ServerOps.Web.Tests`: deploy API endpoint tests

## Key Features

### Host Management

- discover services on Linux and Windows
- start, stop, and restart managed services
- inspect listening TCP ports
- view correlated service topology

### Deployment

- download release asset
- extract to staging
- validate package
- backup current version
- swap staging into current
- start and verify service
- rollback on failure
- persist deployment history

### Exposure

- configure DNS through Cloudflare
- update cloudflared ingress rules
- expose a deployed application with a public URL
- one-click deploy and expose from a release asset

### Operations

- append-only per-operation log files
- deployment history with rollback
- log viewer UI at `/logs/{operationId}`
- minimal deploy API at `POST /api/deploy`

## Configuration

Main configuration is in [`ServerOps.Web/appsettings.json`](/mnt/c/SPC/spc-setup/ServerOps.Web/appsettings.json).

Important settings:

- `Paths:LinuxAppsRoot`
- `Paths:WindowsAppsRoot`
- `Paths:WindowsCloudflaredConfigPath`
- `Domain:DefaultDomainSuffix`
- `DeploymentApiKey`

Environment variables used by infrastructure integrations:

- `STORAGE_CONNECTION_STRING`
- `CLOUDFLARE_API_TOKEN`
- `CLOUDFLARE_ZONE_ID`

## Run Locally

1. Restore and build the solution.
2. Set required configuration and environment variables.
3. Run `ServerOps.Web`.
4. Open the Blazor UI and use:
   - `/` for dashboard
   - `/diagnostics` for service and topology diagnostics
   - `/releases` for deploy, history, rollback, and one-click deploy
   - `/logs/{operationId}` for operation logs

## Deploy API

Endpoint:

```http
POST /api/deploy
```

Headers:

```http
X-API-KEY: <DeploymentApiKey>
Content-Type: application/json
```

Body:

```json
{
  "appName": "phoebus-api",
  "assetUrl": "https://github.com/example/repo/releases/download/v1.0.0/app.zip",
  "hostname": "phoebus.apps.local"
}
```

The API triggers the one-click deploy flow:

1. deploy application
2. optionally generate or use hostname
3. expose application if deployment succeeds

## Bootstrap

Linux bootstrap assets are included at the repository root:

- [`install.sh`](/mnt/c/SPC/spc-setup/install.sh)
- [`serverops.service`](/mnt/c/SPC/spc-setup/serverops.service)

## Documentation

Additional documentation:

- [`docs/architecture.md`](/mnt/c/SPC/spc-setup/docs/architecture.md)
- [`docs/deployment-flow.md`](/mnt/c/SPC/spc-setup/docs/deployment-flow.md)
- [`docs/modules.md`](/mnt/c/SPC/spc-setup/docs/modules.md)
- [`docs/user-guide.md`](/mnt/c/SPC/spc-setup/docs/user-guide.md)
- [`docs/github-action-deploy.yml`](/mnt/c/SPC/spc-setup/docs/github-action-deploy.yml)
