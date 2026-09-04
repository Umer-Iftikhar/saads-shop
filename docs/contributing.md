# Working on this repo

## Branches

`main` is protected and always deployable. Everything else is a short-lived branch off
`main`, merged by pull request.

| Prefix | For |
| --- | --- |
| `feat/` | New capability — `feat/backend-api` |
| `fix/` | Bug fix — `fix/checkout-stock-race` |
| `chore/` | Tooling, scaffolding, dependencies |
| `docs/` | Documentation only |
| `test/` | Tests only |
| `refactor/` | Behaviour-preserving restructuring |

One concern per branch. A branch that touches the database, the API and the frontend is
three branches wearing a coat.

## Commits

Conventional Commits — `type(scope): subject`, imperative mood, no trailing period.

```
feat(orders): lock product rows during checkout to prevent oversell
fix(auth): revoke the whole token family on refresh reuse
docs(database): document the response-code ranges
```

The body explains **why**, not what — the diff already says what.

## Pull requests

Every change lands through a PR, including documentation. A PR states what changed, why,
how it was verified, and what it deliberately leaves out. Small and reviewable beats
complete and enormous.

Merge with **squash** so `main` reads as one commit per logical change.

## Issues

File an issue when something is blocked, deferred or broken — before working around it
silently. Good issues carry:

- What was expected and what actually happened
- The exact error or output
- What has already been tried
- What it blocks

Label with `bug`, `blocked`, `enhancement`, `security`, or `infra`. Link the issue from the
PR that resolves it (`Closes #12`).

## Definition of done

- [ ] Backend validation exists at **both** the API and the stored-procedure layer
- [ ] New stored procedures return `ResponseCode` + `ResponseMessage` as the last result set
- [ ] Unit tests cover the new service logic; integration tests cover the new procedures
- [ ] `dotnet build` produces no warnings; `npm run lint` and `tsc --noEmit` are clean
- [ ] No secret, connection string or token is committed
- [ ] Docs updated when behaviour or contract changed
