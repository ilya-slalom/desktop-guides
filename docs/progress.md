# Project progress

Updated 25 September 2026. This page distinguishes completed P0 evidence
from P1 design and future verification. Story and task acceptance remains in
the [work breakdown](work-breakdown.md).

| Milestone | State | Evidence and next gate |
| --- | --- | --- |
| P0 reader and Windows baseline | Merged into `main` through [PR #1](https://github.com/ilya-slalom/desktop-guides/pull/1) on 25 September 2026, merge commit `8a1f5fdc3468d0533ab8f7ef0a98c323cc3f9bdc`. | [P0 results](p0/results.md) and [task status](p0/implementation-plan.md) record Windows 11 x64 signed install, 14/14 online and physically offline fixture workflows, 23/23 Core tests, native Windows 11 ARM64 Core and 14/14 installed fixture workflows in CI. |
| P0 remaining environment checks | Deferred by the user; not passed. | T01.3 still needs a disposable runtime-free Windows 11 x64 VM to observe actual missing Windows App Runtime/WebView2 failure and recovery. Windows 10 x64 remains untested. These are also bounded P1 release-claim gates. |
| PDF text accessibility | P0 raster probe did not meet the release requirement. | The tagged fixture exposed no document text through the probe's UI Automation surface. [P1 T10.0](p1-technical-design.md#s10--read-pdf-manuals) must select and validate a text-capable distributable path before S10/S17 can close. |
| P1 first usable release | Designed and planned; implementation not started. | [P1 technical design](p1-technical-design.md) and [dependency plan](p1/implementation-plan.md) cover 16 stories and 49 tasks: S03–S17 plus S20. T10.0 is an early PDF gate; S20 manual export/restore moved into P1 because an uninstall removes package local data. |

The next implementation milestone is M0 in the [P1 plan](p1/implementation-plan.md):
portable contracts, managed-path rules, locator codecs, and the PDF engine
decision. Record actual P1 build/test results in `docs/p1/results.md` when
implementation starts. Do not infer production-app compatibility from the P0
fixture harness.
