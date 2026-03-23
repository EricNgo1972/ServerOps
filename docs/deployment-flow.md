# Deployment Flow

The deployment pipeline runs in this order:

1. Download package
2. Extract to staging
3. Validate package
4. Stop service
5. Backup current version
6. Swap staging into current
7. Start service
8. Verify service and health
9. Record deployment history

If verification fails after activation, the system restores the backup, restarts the previous version, verifies it, and records the rollback outcome.

Manual rollback uses deployment history to locate a valid backup, restores that backup into the current slot, restarts the service, verifies recovery, and records the rollback action.
