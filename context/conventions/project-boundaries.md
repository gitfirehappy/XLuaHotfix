# Project Boundaries

Stable project-specific checks for changes that can affect runtime behavior or generated output.

## Approval Before Implementation

Obtain explicit approval before changing any of the following:

- runtime resource loading or hot-update lifecycle;
- build artifact formats or package distribution;
- replacement or coupling of independent backend implementations;
- the bridge between the scripting language and host-language APIs.

The proposal must state the affected boundary, why the existing public surface is insufficient, and how the change will
be verified.

## Generated And Pipeline-Owned Data

- Do not hand-edit generated files or pipeline-owned grouping/output data.
- Change the source configuration or producer, then run the approved generation/build step.
- New script-callable host APIs must update the project's exposure configuration before use.
- Cross-language event registration must follow the existing event system rather than creating a parallel mechanism.
- Use the project's approved loading facade at runtime; do not introduce a second loading path for convenience.
