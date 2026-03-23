# ServerOps AI Agent Rules (Minimal)

## PRIMARY GOAL
Build a minimal cross-platform (.NET 8) Blazor Server app to:
- Discover and manage system services
- Integrate with cloudflared tunnels
- Deploy apps from GitHub releases

---

## ARCHITECTURE

Use simple layered structure:

- Domain → models only
- Application → interfaces + logic
- Infrastructure → OS + external integrations
- Web → UI only

STRICT:
- No cross-layer violations
- No direct shell execution in UI
- All commands go through ICommandRunner

---

## PLATFORM SUPPORT

Must support:
- Linux
- Windows

---

## COMMAND RULES

- No arbitrary shell execution
- Only predefined commands allowed

Allowed:
- systemctl
- sc
- net
- cloudflared
- ss / netstat

---

## DEPLOYMENT RULES

Deployment steps:
1. Stop service
2. Backup current version
3. Deploy new version
4. Start service

---

## UI RULES

- Blazor Server only
- Keep UI simple and functional

---

## CODING RULES

- Use dependency injection
- Use async/await
- No hardcoded paths
- Keep implementation minimal

---

## OUTPUT RULES

- Generate complete working files
- No TODO
- No pseudo-code
