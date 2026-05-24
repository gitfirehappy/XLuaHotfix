# editorui-zh-audit-20260523 Brief

## Goal
Audit the Editor GUI and build/debug text in `Assets/FYAsset/Scripts/Build/**`, replace user-facing English descriptions with concise Chinese, and keep required technical nouns in English where needed, such as `Collector`, `Group`, `Addressables`, `BuildGraph`, `PackageIndex`, and similar terms.

## Scope
- Editor window titles, toolbar labels, section headers, empty-state copy, confirmation text, helper hints, and error/warning messages.
- Debug logs in Editor-only build flow code when they are user-facing or help diagnose workflow issues.
- Keep ambiguous or technically fragile phrases in English when Chinese would reduce clarity.
- Do not change runtime-facing behavior, data models, build artifacts, or Addressables/AB pipeline logic.

## Translation Rule
- Prefer short Chinese phrases.
- Preserve English technical nouns inside Chinese sentences.
- Keep identifiers, class names, file names, and command-line flags in English.
- When a message already uses a precise technical English term that is the actual domain label, retain it.

## Proposed Sub-plan
1. Audit all relevant Editor UI and Debug strings.
2. Rewrite approved strings in place with consistent Chinese wording.
3. Run self-checks for coverage, ambiguity, and unchanged technical terms.
4. Update requirement progress and report verified results.

## Approval Checklist
- [ ] Scope boundary: include only `Assets/FYAsset/Scripts/Build/**`, or also include `Assets/Tools/**` and other Editor-only utilities that surface the same build language?
- [ ] Terminology rule: should `Addressables`, `AB`, `AA`, `BuildGraph`, `PackageIndex`, `Push`, `Diff`, `Validate` stay English as-is inside Chinese sentences?
- [ ] Debug style: should warnings/errors be concise Chinese summaries, or keep a few diagnostic English fragments when the exception text is already precise?
- [ ] UI tone: should the panel copy be strictly short-form Chinese, or slightly descriptive where a one-word label would be too vague?
