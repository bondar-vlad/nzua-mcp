# AGENTS.md

This repository contains a .NET 10 local MCP server for interacting with NZ.UA. Use this file as the short operational guide for AI agents working in this repo.

## Start here

- Read [README.md](README.md) for project overview, setup, security defaults, and tool behavior.
- Read [CONTRIBUTING.md](CONTRIBUTING.md) before changing parsing, auth, or write flows.
- Treat all NZ.UA routes and HTML as unstable internal interfaces; do not add live account data or real journal HTML to the repo.

## Core project shape

- `Program.cs`: server startup, MCP registration (tools + prompts + resources), cross-process login single-flight.
- `Mcp/Tools/`: tool surface for session, journals, forms, marks, lessons, and homework.
- `Mcp/Prompts/`: parameterized teacher workflow prompts grounded in MON order #1427 (17.08.2026).
- `Mcp/Resources/`: journal resources and static reference resources (special marks, grading rules).
- `Nzua/`: browser/auth/session logic, cross-process lock, data models, parser, and API wrappers.
- `tests/NzuaMcp.Tests/`: synthetic fixtures and regression tests for parsing, privacy, storage, and MCP behavior.

## Must-follow conventions

- Keep default privacy enabled: student/teacher names are replaced with stable salted pseudonyms unless `NZUA_SHOW_REAL_NAMES=true` is explicitly set in a trusted environment.
- Respect the read-before-write and verify-after-write pattern for all journal mutations.
- Prefer batch operations via `entriesJson` instead of one tool call per row or item when possible.
- Do not add automatic final-grade recommendations or local guesses for live NZ.UA IDs that must come from the current form.
- Prefer live form values over hard-coded IDs for HUS/NUS/GR scenarios.
- Keep write tools disabled unless `NZUA_ALLOW_WRITES=true` is explicitly set.
- Multiple server processes are a supported scenario: session file is shared under a cross-process lock, manual login is single-flight, browser profiles are per-process. Do not reintroduce shared mutable state without locking.

## Build and test commands

Run these from the repo root:

```powershell
dotnet restore nzua-mcp.sln
dotnet build NzuaMcp.csproj
dotnet test nzua-mcp.sln
```

For browser-based manual checks, install Chromium via Playwright:

```powershell
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

## Safety and data handling

- Never commit real HTML, HAR files, cookies, CSRF values, student names, teacher names, school IDs, screenshots, exports, or personal data.
- Use synthetic values in fixtures and tests.
- Keep security-sensitive files out of the repository; the project intentionally treats session and browser profile data as sensitive local state.
- For changes involving parsing or mutations, add targeted tests before or alongside the fix.

## Editing expectations

- Keep changes minimal and aligned to the existing architecture.
- Prefer updating existing patterns and classes over introducing unrelated abstractions.
- When a fix affects a parser or mutation path, validate it with the relevant test file under `tests/NzuaMcp.Tests/`.
- Do not rewrite the design for unrelated cleanup; focus on the current task.

## Before finishing

- Run the relevant validation command, typically `dotnet test nzua-mcp.sln`.
- Confirm the change matches the project’s privacy, safety, and write-policy rules.
- If the task touches live NZ.UA behavior, ensure the instructions in [README.md](README.md) and [CONTRIBUTING.md](CONTRIBUTING.md) are still followed.

## Helpful pointers

- Configuration and tool contract details are documented in [README.md](README.md).
- Security, contributor expectations, and data rules are described in [CONTRIBUTING.md](CONTRIBUTING.md).
- The project is intentionally not an official NZ.UA API client; treat every route and selector as implementation detail rather than contract.
