# AI Collaboration Guide

## Core Rules

- Respond to the developer in Chinese.
- Use plain, objective language. Describe observable behavior, constraints, ownership, lifetime, ordering, and reasons in terms a developer can verify.
- Keep this file to cross-project collaboration rules. Project names, paths, types, modules, architecture decisions, tool versions, current status, and historical cases belong elsewhere.
- Do not modify code or workflow files without explicit developer approval.
- Keep changes scoped and aligned with the existing repository conventions.

## Workflow

Non-trivial work follows `grill -> plan -> implement`.

- Read the current state before asking a design question.
- Ask one decision at a time. Include a recommended answer and a rebuttal test.
- The plan starts with purpose, constraints, and success criteria.
- Do not implement until the plan is explicitly approved.
- Split work into independently testable and reviewable tasks.
- Use subagents sparingly. Run at most two concurrent subagents unless the developer requests or explicitly decides otherwise.
- Keep `requirements/progress.txt` concise and current. Append new entries only at the end of the file; never insert at the head or in the middle. Read the end of the file for the latest state. Put detailed analysis and verification evidence in the active plan or review report.

## Testing And Verification

- Start a bug fix with a reproduction or the smallest runnable check that fails for the reported behavior.
- Locate the root cause before changing code. After three failed fix attempts, stop patching and reopen the design.
- Use the narrowest verification that proves the changed behavior; record the command and result in the relevant plan or review.
- Do not claim completion, passing, or correctness without fresh verification output.
- When blocked or uncertain, stop and ask instead of guessing.

## Change Safety

- Check `git status --short` before editing. Do not overwrite, revert, or clean changes not created by the current task.
- Before deleting, moving, batch-rewriting files, changing ignore rules, or rewriting history, state the impact and obtain separate explicit approval.
- Verify according to the change type: for code, verify behavior; for documentation, check links and factual boundaries; for indexes, check paths and duplicates. Cover the affected scope.

## Knowledge Boundaries

- Source and tests define current behavior.
- `context/` stores stable, verified, reusable knowledge. It is not a current-status board, plan queue, TODO list, or document archive.
- `requirements/` stores plans, approvals, concise progress, and reviews.
- `docs/` stores human-facing explanations, current architecture, design history, and tool instructions.
- Keep external-source notes separate from project rules and current implementation facts.

## Comments

- Add comments for non-obvious behavior, constraints, ownership, lifetime, ordering, or reasons.
- Write comments in human-readable, objective language. Explain technical terms when they are not obvious from the code.
- Do not hide meaning behind unexplained shorthand, metaphors, compressed arrows, or internal nicknames.
- Keep comments concise. Put design history, broad rationale, and workflow instructions in documentation.
- Follow the language and syntax requirements of the source language, but do not impose a language-specific comment style on this file.

## Git

- Keep each commit focused on one logical change.
- Use an English type prefix followed by one concise Chinese summary, unless the repository has an explicit established exception.
- Before committing, show the proposed scope, paths, and message and obtain explicit approval.
- Do not commit secrets, editor state, build caches, or generated local output unless the repository explicitly tracks it.
- Verify the committed result again. Ask before deleting worktrees or branches.
