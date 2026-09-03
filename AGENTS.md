# Project Agent Instructions

## Scope and Precedence

This file is the repository-level entrypoint for coding agents.

Read `.agents/docs/project.md` before non-trivial
work. Repository-specific commands, constraints, and narrower instructions take
precedence over these template defaults.

## Project Workflow

For non-trivial work, follow:

- `.agents/docs/workflow.md`
- `.agents/docs/testing.md`

Coding conventions:

- `.agents/docs/coding-style.md`

For tracked Git work, follow:

- `.agents/docs/issue.md`
- `.agents/docs/commit.md`
- `.agents/docs/pull-request.md`

Use project-local skills when installed and applicable. Skill instructions
define their own triggers, formats, and output paths.

## Logging the SDK in against a local server

The SDK does not log in by itself. `ArtelLoopbackLogin` opens `<home>/sdk-login`
in a browser, and that page trades an existing console session for a one-time
code — `SdkLoginPage` returns early unless the visitor is already signed in. A
local stack has no registered GitHub OAuth app, so that session does not exist
and the overlay's login button cannot finish.

Mint the console session in the browser first, then press it:

```bash
.claude/skills/artel-jwt/mint-jwt.py --sub <app_user.id> --ttl 8h --format browser
```

Paste that line into the DevTools console on the artel-home tab. The rest of the
flow — the code, the loopback callback, `/api/auth/sdk/token` — runs unchanged.

Do not try to plant an `artel-sdk` token in the SDK's own store instead.
`ArtelSecretStore` keeps it in the OS secret store (DPAPI on Windows), not in a
file you can write. To exercise an `/api/sdk/**` endpoint by hand, mint that
token and send it yourself:

```bash
.claude/skills/artel-jwt/mint-jwt.py --sub <app_user.id> --audience sdk --format header
```

The `artel-jwt` skill covers the rest. It mints for a local server only.
