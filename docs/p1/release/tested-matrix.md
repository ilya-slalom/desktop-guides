# Tested matrix

What each release was tested on before it was published (T17.3). A target
is supported only when it has a passing row here. The run's full evidence
stays with its run; this page holds the sanitized summary.

## How a row is made

The `rc` group of the installed harness runs on the release's draft
assets ([design](../t17-3-release-candidate-design.md)):
1. install the previous release;
2. build a library through the UI;
3. upgrade in place to the candidate;
4. read, resume and complete;
5. relaunch with the app's network blocked;
6. export, remove, and restore.

Every stage must pass.

## 1.0.1

Not run yet.

## Not tested

| Target | Why |
| --- | --- |
| A clean Windows 11 x64 machine without the Windows App Runtime | The runtime-free VM check is T17.8, in the final milestone. |
| Windows 10 x64 | Not advertised. |
| Windows 11 ARM64 | Not released; it needs its own native full-workflow result. |
| The portable build's own release gates | First run without installation, data location and replacing the exe are defined separately. Its scenario groups run in CI on every change. |
