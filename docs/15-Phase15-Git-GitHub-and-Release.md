# Phase 15 — Git, GitHub & Final README

> **Phase status:** ✅ Implemented (2026-11-24) — final README written and pushed

## 1. Repository hygiene
| File | Purpose |
|---|---|
| `.gitignore` | Standard .NET template (`bin/`, `obj/`, `.vs/`, `TestResults/`, `*.user`) + `**/emails/` (dev emails contain tokens) + `UIimages/` (local design references) |
| `.gitattributes` | `* text=auto` — consistent line endings across Windows/Linux |
| `global.json`, `dotnet-tools.json` | Reproducible SDK and tool versions for every clone |
| Secrets | Never in Git: User Secrets locally, environment variables in production |

## 2. Commands used to create and publish the repository
```powershell
git init -b main
git add <explicit paths>            # review `git status --short` first — never blind `git add -A`
git commit -m "Phase N: ..."
gh auth login                       # GitHub CLI login (browser)
gh auth setup-git                   # let plain `git push` use the gh credentials
gh repo create 01-MonolithicArchitecture-TaskManagementSystem --public --source . --remote origin --push
git push                            # after every phase
```
Alternative without the GitHub CLI: create the repo on github.com, then
`git remote add origin https://github.com/<user>/<repo>.git` and `git push -u origin main`.

## 3. Daily workflow
```powershell
git status --short                  # what changed? any unknown files?
git diff                            # review
git add src tests docs README.md
git commit -m "Short imperative summary"
git push
```

## 4. Lessons learned (history rewrites)
| Incident | Fix |
|---|---|
| Commit messages contained an unwanted co-author line | `git filter-branch --msg-filter` + `git push --force-with-lease` |
| `git add -A` published 4 local design images | Backed up the folder, `git filter-branch --index-filter 'git rm -r --cached --ignore-unmatch UIimages'`, gitignored the folder, force-pushed |

Notes: rewriting history changes commit SHAs; always back up first (`filter-branch` also removed the folder from the working tree — the backup restored it); use `--force-with-lease`, never plain `--force`; GitHub may keep orphaned commits reachable by direct SHA until GitHub Support purges them.

## 5. Final state
- 16 documents in `docs/`, complete README (overview, architecture, diagrams, ER, workflow, setup, migrations, 40 endpoints, testing, pros/cons, interview questions).
- 138 unit + 29 integration tests passing, 0 build warnings.
