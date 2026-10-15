# P2 results

Merged P2 work, in the [P1 results](../p1/results.md) format.

| PR | Merged | Commit | Task | Final CI run | Core | Infra | Record | Caveats |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [#116](https://github.com/ilya-slalom/desktop-guides/pull/116) | 2026-10-15 | `d6ca0ec` | T18.1 (PR b) | [38684615275](https://github.com/ilya-slalom/desktop-guides/actions/runs/38684615275) | 1210 | 790 | [t18-1-search-design](t18-1-search-design.md#pr-b-verification-record) | HTML search. One re-run: `library-missing` (portable `core`) failed once on focus, outside this change. Keys inside an HTML page are #115. |
| [#113](https://github.com/ilya-slalom/desktop-guides/pull/113) | 2026-10-15 | `c6546c0` | T18.1 (PR a) | [38657274295](https://github.com/ilya-slalom/desktop-guides/actions/runs/38657274295) | 1190 | 790 | [t18-1-search-design](t18-1-search-design.md#pr-a-verification-record) | The search contract, TXT search and the find bar, with the `navigation` group. The `html` shard passed on a re-run; its MSIX `html-position-light` failure was not seen again. |
