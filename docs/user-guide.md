# User Guide

ServerOps must run with elevated privileges:

- `Administrator` on Windows
- `root` on Linux

Without that, deployments that install or manage services will fail.

## Deploy an application

Open the Releases page and enter the repository, application name, and release asset URL. If you want a public address, provide a hostname or enable automatic hostname generation. Start the one-click deploy action and wait for the result card to update.

## Check deployment result

After deployment, review the result section on the Releases page. It shows the deployment status, current stage, message, and whether public exposure succeeded.

## Access application URL

If exposure succeeded, the Public URL field shows the address to use in a browser. If the field is empty, the app was deployed but not exposed publicly.

## Rollback a deployment

In the Deployment History table, find an earlier successful deployment and select Rollback. The latest deployment cannot be rolled back from the UI, and items without a stored backup are disabled.

## Troubleshooting

If the service is not starting, review the deployment message and the operation log for the deployment ID. A failed start usually means the app files are invalid or the service configuration needs attention.

If deployment fails while registering or controlling a service, first confirm that ServerOps itself is running as `Administrator` on Windows or `root` on Linux.

If there is a port conflict, another process is already listening on the required port. Stop the conflicting service or change the app configuration before deploying again.

If the domain is not resolving, the deployment may have succeeded while exposure failed. Check the exposure status and public URL on the Releases page, then verify the Cloudflare and tunnel configuration.
