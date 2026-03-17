# 项目一键初始化提示词

融合三层体系：小彭老师 dotfiles-opencode（自动化流水线）+ shicheng AGENTS.md（跨会话记忆）+ oh-my-opencode Sisyphus（智能编排）。

---

## 一、全新项目初始化

**使用说明**：在空目录启动 OpenCode 会话，复制以下提示词直接发送。

````markdown
# Task: Initialize New Project with Tri-Layer Architecture

You are authorized to autonomously explore the environment, inspect system tools, and make
reasonable technical decisions without asking me first.

## Step 1 - Confirm Project Info

Before anything else, ask me to confirm:
- **Project name**
- **Primary tech stack** (e.g. Python + FastAPI / TypeScript + Node / C++ + CMake)
- **Project type** (CLI / Web API / Library / Desktop App / Other)
- **Visibility** (Public / Private)

Wait for my confirmation before proceeding to the next steps.

## Step 2 - Foundation (setup-fresh-project)

Use `skill(name="setup-fresh-project")` to initialize the base structure:
- `git init` and create a suitable `.gitignore`
- Create formatter/linter configs for the chosen stack:
  - Python: `pyproject.toml` (with `[tool.ruff]`) + `pyrightconfig.json`
  - TypeScript: `biome.json` or `.eslintrc`
  - C/C++: `.clang-format` + `CMakeLists.txt` with `CMAKE_EXPORT_COMPILE_COMMANDS=ON`
- Create standard directories: `src/`, `tests/`, `docs/`
- Create `README.md` with project overview, build instructions, and test instructions
- Verify required tools are available (`uv`, `ruff`, `node`, etc.); if missing, tell me explicitly

## Step 3 - Knowledge Context Scaffold (shicheng AGENTS.md layer)

Create the persistent memory structure:

```bash
mkdir -p context/architecture context/business context/experience
mkdir -p requirements
```

Create the following files:

### context/architecture/INDEX.md

Card-based index format:

```markdown
# Architecture Knowledge Index

(Empty - populate as architectural decisions are made)
```

### context/business/INDEX.md

```markdown
# Business Knowledge Index

(Empty - populate as domain knowledge is discovered)
```

### context/experience/INDEX.md

```markdown
# Experience Knowledge Index

(Empty - populate as lessons are learned)
```

### context/architecture/tech-stack.md

Document the actual chosen tech stack and rationale:
- Language and version
- Key dependencies/frameworks and why they were chosen
- Build/test toolchain
- Deployment target (if known)

## Step 4 - Project-Level AGENTS.md

Create `AGENTS.md` in the project root with the following structure
(fill in actual project name and details):

```markdown
# {Project Name} - AI Collaboration Guide

## Project Overview

{One paragraph: what this project is, the tech stack, and core purpose.}

## Collaboration Principles

- Understand the problem before writing code
- Say "I don't know" instead of making things up
- Show reasoning before conclusions on important decisions
- Consider maintainability in every change
- Respond in Chinese; write code comments and docs in English

## Knowledge Base (context/)

- Before starting any task, check `context/` for relevant existing knowledge
- When discovering new lessons or pitfalls, propose writing them to the appropriate domain
- Only put verified knowledge in `context/`; tag unconfirmed items with `[UNVERIFIED]`

## Requirement Workflow

1. New requirement -> create `requirements/{id}/brief.md` (what + why, 10-20 lines max)
2. AI generates `requirements/{id}/plan.md`; confirm before starting
3. During development -> append key events to `requirements/{id}/progress.txt`
4. Pitfalls/discoveries -> record in `requirements/{id}/notes.md`
5. After completion -> migrate valuable lessons to `context/`

### progress.txt Event Types

| Type | Meaning | Example |
| --- | --- | --- |
| `start` | Beginning work | `start: design data model` |
| `done` | Completed work | `done: auth middleware with role+permission` |
| `decision` | A choice was made | `decision: use Casbin over custom RBAC (reason: maturity)` |
| `blocked` | Paused waiting | `blocked: need to confirm permission granularity` |
| `next` | Next action item | `next: write migration script for existing admin users` |

## Session Recovery

When a new session starts and I say "continue" or "继续":
1. Read the latest entries in `progress.txt`
2. Read `plan.md` for overall context
3. Summarize in 2-3 sentences: what was done and what comes next
4. Wait for my confirmation before proceeding

## Coding Standards

- Use 4-space indentation (new projects)
- For existing projects: detect existing style from config files first
- Use `uv` for all Python tasks; fallback to `python`/`pip` if unavailable
- Write one-off analysis scripts to `/tmp/`; do not pollute the project
- Keep `git status` clean; update `.gitignore` for generated artifacts

## Git Standards

- Summarize changes before committing; wait for confirmation
- Commit messages: imperative mood, explain the "why"
- Do not commit directly to main if a branch strategy is in use

## Project-Specific Rules

{Add project-specific rules here as the project evolves.}
```

## Step 5 - Verification

Use `skill(name="verification-before-completion")` to confirm:
- [ ] Git repository initialized
- [ ] `.gitignore` exists and covers the tech stack's generated files
- [ ] Formatter/linter config exists
- [ ] `src/` and `tests/` directories exist
- [ ] `README.md` exists with no placeholder links or hard-coded paths
- [ ] `context/` has architecture, business, experience sub-directories each with `INDEX.md`
- [ ] `context/architecture/tech-stack.md` exists with actual content
- [ ] `requirements/` directory exists
- [ ] Project-level `AGENTS.md` exists with project name filled in

Report any missing items and fix them before continuing.

## Step 6 - Initial Commit

Delegate to `@committer` to create the first commit:

Commit message: `feat: initialize project structure with AGENTS.md collaboration framework`

Include all created files.

---

After all steps complete, print a summary table of what was created.
````

---

## 二、存量项目接入版（已有代码库）

**使用说明**：接手已有项目，只注入上下文体系，不重建文件结构。

````markdown
# Task: Inject AGENTS.md Knowledge Framework into Existing Project

I want to start collaborating on this existing codebase using the AGENTS.md knowledge system.
Do NOT run setup-fresh-project. Focus only on context initialization.

You are authorized to explore the codebase read-only before doing anything.

## Step 1 - Analyze Existing Project

Run read-only exploration to identify:
- Tech stack and key dependencies (from `package.json` / `pyproject.toml` / `CMakeLists.txt` etc.)
- Existing directory structure and conventions
- Existing indentation style and linter configs
- Any existing documentation

## Step 2 - Create Knowledge Structure

```bash
mkdir -p context/architecture context/business context/experience
mkdir -p requirements
```

Create `context/architecture/INDEX.md`, `context/business/INDEX.md`,
`context/experience/INDEX.md` - each as empty card-based index files.

Create `context/architecture/tech-stack.md` based on your analysis of the existing codebase:
- Document what stack is actually in use
- Note any patterns or conventions you observed

## Step 3 - Update .gitignore

Check `git status` for untracked noise files. Update `.gitignore` to exclude:
- Build artifacts, caches, temp files
- IDE/editor directories (`.idea/`, `.vscode/` if not tracked)
- Any other project-specific generated files

## Step 4 - Create Project-Level AGENTS.md

Create `AGENTS.md` in the project root. Populate it with:
- Actual project name and one-paragraph description (based on your analysis)
- Collaboration principles, knowledge base workflow, requirement workflow
- Session recovery protocol
- **Detected** coding standards (existing indent style, linter, test framework)

## Step 5 - Verify and Commit

Confirm:
- [ ] `context/` structure is in place with `INDEX.md` files
- [ ] `context/architecture/tech-stack.md` has real content
- [ ] `requirements/` directory exists
- [ ] `AGENTS.md` exists with actual project name filled in (no generic placeholders)
- [ ] `.gitignore` is updated and `git status` shows no unexpected noise

Then delegate to `@committer`:

Commit message: `chore: integrate AGENTS.md collaboration framework`
````

---

## 三、初始化完成检查清单

初始化结束后，项目根目录应有如下结构：

| 路径 | 类型 | 说明 |
| --- | --- | --- |
| `AGENTS.md` | 文件 | AI 协作规范，含项目简介和会话恢复协议 |
| `context/architecture/INDEX.md` | 文件 | 架构决策知识索引 |
| `context/architecture/tech-stack.md` | 文件 | 技术栈选型及理由 |
| `context/business/INDEX.md` | 文件 | 业务领域知识索引 |
| `context/experience/INDEX.md` | 文件 | 踩坑经验知识索引 |
| `requirements/` | 目录 | 需求追踪根目录（初始为空） |
| `src/` | 目录 | 源代码（setup-fresh-project 创建） |
| `tests/` | 目录 | 测试代码（setup-fresh-project 创建） |
| `docs/` | 目录 | 文档（setup-fresh-project 创建） |
| `.gitignore` | 文件 | 过滤构建产物和缓存 |
| `README.md` | 文件 | 项目说明（含构建/测试命令） |
| `pyproject.toml` 或等效配置 | 文件 | 格式化/类型检查配置 |

---

## 四、初始化之后的标准工作流

```
新需求
  │
  ├─ 创建 requirements/{id}/brief.md（做什么 + 为什么）
  │
  ├─ @brainstorm   生成 tasks.json（含分层测试计划）
  │
  ├─ @executor     逐任务驱动 @worker 执行
  │       │
  │       ├─ @worker 用 tdd-workflow skill 先写失败测试再实现
  │       ├─ @worker 完成后写 PROGRESS.txt
  │       └─ @committer 提交
  │
  └─ 需求完成后：有价值的经验迁移到 context/{domain}/

新会话恢复
  └─ 发送"继续 {id}" -> AI 读 progress.txt -> 2-3 句话同步状态 -> 等确认
```

---

## 五、提示词速查（按场景）

| 场景 | 发送的命令 |
| --- | --- |
| 全新项目初始化 | 使用上方「全新项目」提示词 |
| 已有项目接入体系 | 使用上方「存量项目」提示词 |
| 开始新功能开发 | `@brainstorm 我想实现 [功能描述]` |
| 自动执行任务列表 | `@executor` |
| 恢复上次进度 | `继续 {requirement-id}` |
| 深度分析项目结构 | `/repo-analyser` |
| 代码审查 | `/code-review [文件路径]` |
| 遇到架构难题 | 咨询 `oracle`（昂贵，仅复杂问题使用） |
