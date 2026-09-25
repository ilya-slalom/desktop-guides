# Desktop Guides

A Windows desktop application in development for collecting, reading, and
tracking written game guides. The UI uses WinUI 3.

See the [initial requirements and high-level design](docs/initial-design.md)
for the proposed scope, reader architecture, data model, and delivery milestones.
The [work breakdown](docs/work-breakdown.md) maps those requirements to user
stories, implementation tasks, and testable technical requirements.
The [P0 technical design](docs/p0-technical-design.md) details the Windows
baseline and TXT, HTML, and PDF reader experiments.

The P0 implementation includes a WinUI 3 reader probe for TXT, static HTML,
and PDF, a portable core library, self-authored fixtures, unit tests, and
Windows package builds. It is an evaluation harness; library and tracking
features are planned for P1. See the [P0 implementation plan](docs/p0/implementation-plan.md),
[Windows build instructions](docs/p0/toolchain.md), and
[initial Windows observations](docs/p0/results.md).
