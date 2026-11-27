# Agent Instructions

This project uses **bd** (beads) for issue tracking and persistent memory. Run `bd prime` for full workflow context.

> **Single source of truth (2026-11-24):** `docs/PLAN.md` and `docs/PROGRESS.md` were retired and deleted.
> All plans, progress, remaining work and lessons live in **bd** only — `bd ready`, `bd list`, `bd stats`,
> `bd remember` / `bd memories`. Their full text before deletion is archived as closed issues
> `FHeroes2Sharp-3so` (PLAN) and `FHeroes2Sharp-jwk` (PROGRESS); any `§x` citation in code or docs refers to
> those issues. Do not recreate plan/progress markdown files or ad hoc `MEMORY.md` / `TODO.md` files.

> **Architecture in one line:** Issues live in a local Dolt database (`.beads/dolt/`); cross-machine sync uses `bd dolt push/pull` stored under `refs/dolt/data` on your git remote (separate from `refs/heads/*`). `.beads/issues.jsonl` is a passive export only — do not treat it as the source of truth or run `bd import` during normal operation. See [sync-concepts](https://github.com).

## Quick Reference

```bash
bd ready                # Find available work
bd show <id>            # View issue details
bd update <id> --claim  # Claim work atomically
bd close <id>           # Complete and close work
bd prime                # Refresh Beads context when stale
```

## Non-Interactive Shell Commands

**ALWAYS use non-interactive flags** with file/remote operations to avoid hanging on confirmation prompts:
- File ops: `cp -f`, `mv -f`, `rm -f`, `rm -rf`, `cp -rf`
- Remote/Pkg ops: `scp -o BatchMode=yes`, `ssh -o BatchMode=yes`, `apt-get -y`, `HOMEBREW_NO_AUTO_UPDATE=1 brew`

---

## Workflow Rules (Cline / CodeBuddy)

### 1. 快捷执行指令 ("继续" / "go")
当用户的输入仅为“**继续**”、“**go**”、“**continue**”或“**next**”（不带其他具体指令）时，无需向用户二次确认，立即按以下标准流程执行：
1. **检查断点**：运行 `bd list --status in_progress`。若已有进行中的 Issue，执行 `bd show <id>` 读取详情并从断点继续开发。
2. **拉取新任务**：若无进行中任务，运行 `bd ready` 获取无阻塞、最高优先级的就绪 Issue。若列表为空，运行 `bd list --status open` 汇报被阻塞的 Issue 及原因并停止。
3. **认领并评估**：执行 `bd update <id> --claim` 并运行 `bd show <id>` 阅读完整需求；先评估体量是否过大（见下方拆分规则），再开始写代码。
4. **验证、关闭并暂存（单任务强制闭环）**：
   - 跑通编译/测试后，**必须立即运行 `bd close <id> --reason "完成内容与测试结果"` 关闭当前 Issue**。
   - 运行 `git add` 将本次修改的文件放入暂存区（禁止 `git commit` / `git push`）。
   - 向用户汇报已关闭的 Issue ID 与改动摘要，**立即停止，严禁擅自开启下一个 Issue**。

### 2. 任务体量评估与自动拆分规则
在认领或执行任何 `feature` / `task` 类型的 Issue 时，若发现以下任一情况，**严禁在单个 Issue 中硬编码完成**，必须立即拆分：
1. **上下文消耗预警**：单个任务无法在一次干净的会话（Context Window）内完成并验证测试。
2. **发现隐藏依赖**：实现过程中发现了未预料到的前置重构或独立子需求。

**拆分执行步骤：**
- **开工前发现过大**：
  1. 升级为 Epic：`bd update <current-id> --type epic`
  2. 创建最小可测试子任务：`bd create --parent <current-id> --type task --title "子任务标题"`
  3. 建立先后依赖（如有）：`bd dep add <child-2-id> <child-1-id>`
  4. 重新运行 `bd ready` 领取第一个无阻塞的子任务，不要停留在原 Epic 上。
- **开发到一半发现太大**：
  - 保留当前 Issue 完成已写部分，将剩余需求拆为新 Issue（`bd create --title "..."`），并标记来源：`bd dep add <new-id> <current-id> --type discovered-from`，随后照常验证并 `bd close <current-id>` 关闭当前部分。

---

## Session Completion & Git Policy (Stage-Only)

**Strict Git Boundary:**
- **ALLOWED**: `git status`, `git diff`, `git add <files>` (staging completed changes for user review).
- **FORBIDDEN**: **NEVER** run `git commit`, `git push`, or `bd dolt push` under any circumstances unless explicitly commanded in the current turn.

**Mandatory Completion Checklist (Every Issue):**
1. **File follow-ups**: Create beads for any discovered bugs or remaining work (`bd dep add <new> <current> --type discovered-from`).
2. **Run quality gates**: Run builds, tests, and linters if code changed; ensure all pass before closing.
3. **MUST Close Issue**: Run `bd close <id> --reason "完成了xxx，通过了xxx测试"` — **never leave a finished Issue in `in_progress`**.
4. **Stage changes**: Run `git add` on the modified/created files for this Issue.
5. **Hand off & Stop**: Report the closed Issue ID, staged files, validation results, and a preview of the next `bd ready` item, then wait for the user.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:46cd31e7 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/core-concepts/sync-concepts.md for details and anti-patterns.

## Agent Context Profiles

The managed Beads block is task-tracking guidance, not permission to override repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** - Create beads for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   bd dolt push
   git push
   git status
   ```
5. **Hand off** - Summarize changes, validation, issue status, and any blocked sync/commit/push step

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
<!-- END BEADS INTEGRATION -->

<!-- BEGIN BEADS CODEX SETUP: generated by bd setup codex -->
## Beads Issue Tracker

Use Beads (`bd`) for durable task tracking in repositories that include it. Use the `beads` skill at `.agents/skills/beads/SKILL.md` (project install) or `~/.agents/skills/beads/SKILL.md` (global install) for Beads workflow guidance, then use the `bd` CLI for issue operations.

### Quick Reference

```bash
bd ready                # Find available work
bd show <id>            # View issue details
bd update <id> --claim  # Claim work
bd close <id>           # Complete work
bd prime                # Refresh Beads context
```

### Rules

- Use `bd` for all task tracking; do not create markdown TODO lists.
- Run `bd prime` when Beads context is missing or stale. Codex 0.129.0+ can load Beads context automatically through native hooks; use `/hooks` to inspect or toggle them.
- Keep persistent project memory in Beads via `bd remember`; do not create ad hoc memory files.

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/core-concepts/sync-concepts.md for details and anti-patterns.
<!-- END BEADS CODEX SETUP -->
