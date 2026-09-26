---
name: todo-dev
description: Use when an item from README's `## TODO` backlog is to be picked up and implemented — the user says "take a TODO item", "do something from the TODO", names a backlog line to work on, or invokes /todo-dev.
---

# A TODO item, start to finish

Turn one line of README's `## TODO` into one reviewed commit, on its own branch, in its own
worktree, with the box ticked and the version bumped.

**The pipeline below is the deliverable.** Nine steps, in order. Step 3 is the one that gets
skipped: work done straight on `dev` in the shared checkout cannot be abandoned, cannot be
reviewed as a branch, and collides with the other sessions that run in that same checkout while
`dev` moves under you.

## The pipeline

### 1. Resolve the item

The arguments you were invoked with name the item — match them against the unchecked `- [ ]`
lines under `## TODO` in `README.md`. No arguments → the first `- [ ]` line. Quote the resolved
line back in one sentence before touching anything. Nothing matches, or two match equally well →
stop and ask which one.

### 2. Classify, and state the verdict

**Hard** if any of these holds: it touches more than two projects; it needs an EF migration or a
new endpoint; its own text defers a decision ("needs a…", "decide…", "find out why…"); or real
alternatives exist whose consequences differ. **Easy** otherwise.

- Hard → invoke `superpowers:brainstorming`, and wait for design approval before any code.
- Easy → go to step 3.

Say which verdict you reached and why, in one line. Don't ask for it — state it; the user
overrides it if they disagree.

### 3. Isolate

`EnterWorktree`, on a new branch off the current `dev` tip: `feat/<slug>` for a feature,
`fix/<slug>` for a bug, the slug from the item. Every edit from here until step 9 happens in the
worktree.

### 4. Do the work

`superpowers:test-driven-development` — a failing test first — when the item is a bug or new
logic. Not for design tokens, copy, or docs. `CLAUDE.md` conventions hold throughout: English in
code, comments and commits; component CSS uses only `var(--…)`; buttons via `.btn` + a variant;
numbers via `formatInt`/`wordsLabel`; no new npm dependency without asking.

### 5. Tick the box

In the worktree's `README.md`, flip that item's `- [ ]` to `- [x]`, keeping the line's own text.
If the work deferred something new, add it as a fresh `- [ ]` line in the same change.

### 6. Bump the version

`Directory.Build.props`, the `<Version>` element, by README → Versioning:

| The work is a | Version |
|---|---|
| `feat` | minor + 1, patch → 0 |
| `fix` or `perf` | patch + 1 |
| anything else (docs, refactor, chore, test) | **unchanged** |

Which row applies is decided by the conventional subject you will write in step 8, not by the
TODO line's wording. Edit the file — it is the contract, and `web/vite.config.ts` reads the same
element.

### 7. Verify, and show the output

```bash
cd web && npm run lint && npm test && npm run build
dotnet test
```

Paste the real tail of each. Green claimed without output is not verification — see
`superpowers:verification-before-completion`.

### 8. Squash to one commit

Committing as you go while working is fine. Once the work is done, `git reset --soft <the dev tip
you branched from>` and commit once: a conventional subject matching the bump in step 6, a body
saying why, and the `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>` trailer. The user
reviews one commit, never a series.

### 9. Ask how to land it

First rebase onto dev's current tip, then **re-run step 7** (a new base can add dependencies) and
**re-derive step 6 from the new base** — dev may have bumped the version while you worked, which
makes the number you wrote stale or conflicted.

Then show the diff and ask which one:

- **Merge into `dev` locally** → `ExitWorktree` with `action: "keep"` → in the shared checkout
  confirm `git status` is clean and it is on `dev` → `git merge --ff-only <branch>` → re-run
  step 7 on the merged result → `git worktree remove` + `git branch -d <branch>`.
- **Leave it on the branch** → `ExitWorktree` with `action: "keep"`, then report the branch name
  and the worktree path.

**Never push.** Not after a merge, not "while we're here". A push is always its own request.

## Red flags — stop and go back

| Thought | Reality |
|---|---|
| "One-line change, a worktree is overkill" | A one-line change on `dev` still lands under another session's feet. Step 3 is not sized to the diff. |
| "I'll branch later, let me just look at it working" | Editing in the shared checkout *is* the failure. Worktree first. |
| "I'll tick the box now and run the suites after" | `[x]` means verified. Step 7's output comes first. |
| "They can squash it themselves" | One commit is the deliverable. Squash it. |
| "It's `dev`, it hasn't moved" | Check `git log`, don't assume. It moved last time. |
| "Merged and green — pushing is the obvious next step" | It is a separate request. Stop. |
| "Docs item, I'll bump the patch anyway" | Anything that is not `feat`/`fix`/`perf` leaves the version alone. |

## Common mistakes

- **Marking a different line `[x]`** than the one worked on — quote the line in step 1 so the
  match is visible and correctable.
- **Rewriting the TODO line** while ticking it. Flip the box, keep the text; a reworded item can
  no longer be found by the person who wrote it.
- **Squashing with `reset --soft HEAD~N`** after a rebase, when `N` no longer means what it did.
  Reset to the base commit by hash.
- **Forgetting the new `- [ ]` line** when the work defers something. `CLAUDE.md` requires it in
  the same set of changes, not in a follow-up.
