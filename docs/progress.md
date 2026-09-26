# Project progress

Updated 26 September 2026. This page distinguishes completed P0 evidence
from P1 implementation and future verification. Story and task acceptance remains in
the [work breakdown](work-breakdown.md).

| Milestone | State | Evidence and next gate |
| --- | --- | --- |
| P0 reader and Windows baseline | Merged into `main` through [PR #1](https://github.com/ilya-slalom/desktop-guides/pull/1) on 25 September 2026, merge commit `8a1f5fdc3468d0533ab8f7ef0a98c323cc3f9bdc`. | [P0 results](p0/results.md) and [task status](p0/implementation-plan.md) record Windows 11 x64 signed install, 14/14 online and physically offline fixture workflows, 23/23 Core tests, native Windows 11 ARM64 Core and 14/14 installed fixture workflows in CI. |
| P0 remaining environment checks | Deferred by the user; not passed. | T01.3 still needs a disposable runtime-free Windows 11 x64 VM to observe actual missing Windows App Runtime/WebView2 failure and recovery. Windows 10 x64 remains untested. These are also bounded P1 release-claim gates. |
| PDF text accessibility | M0 prototype decision reached; production gate open. | [T10.0](p1/pdf-decision.md) selected a native preview plus PdfPig text path after the tagged paragraph appeared in Windows UI Automation and keyboard selection. T10.1–T10.3 and T16.3 still need a production adapter, Narrator record, and installed offline checks before S10/S17 close. |
| P1 M0 | Implemented in [PR #3](https://github.com/ilya-slalom/desktop-guides/pull/3) for review; not yet merged. | [M0 results](p1/results.md) record storage contracts/schema, managed path tests, typed reader contract, locator codecs, and the PDF decision. A review follow-up fixed retry after interrupted empty SQLite initialization; the locked Windows 11 x64 run passed 61 Core and 13 Infrastructure tests. Windows 11 x64 PDF UIA/keyboard and app-specific outbound-blocked runs passed; the earlier [PR CI](p1/results.md#pr-ci-evidence) passed x64 and native ARM64 headless suites, both package builds, and the installed ARM64 P0 regression. |
| P1 first usable release | M1–M6 not started. | [P1 technical design](p1-technical-design.md) and [dependency plan](p1/implementation-plan.md) cover 16 stories and 49 tasks: S03–S17 plus S20. S20 manual export/restore is in P1 because an uninstall removes package local data. |

The next implementation milestone is M1 in the [P1 plan](p1/implementation-plan.md):
schema migration, startup reconciliation, the production shell, and package
identity. Do not infer production-app compatibility from the P0 fixture
harness or the M0 PDF experiment.
