# Architecture

ServerOps uses a simple layered structure:

- Domain: records and enums only.
- Application: interfaces, DTOs, and orchestration logic.
- Infrastructure: operating system integration, external services, storage, and runtime implementations.
- Web: Blazor UI and the minimal deploy API.

Operational requirement:

- ServerOps must run as `Administrator` on Windows or `root` on Linux because Infrastructure performs service registration, service control, and Linux runtime-user management.

Module 9 covers the deployment pipeline, including staged deployment, verification, and rollback-on-failure.

Module 10 adds deployment history persistence and operator rollback capability.

Module 11 adds one-click orchestration that combines deployment and public exposure.
