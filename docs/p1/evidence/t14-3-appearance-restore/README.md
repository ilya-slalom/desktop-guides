# T14.3 appearance restore evidence

From full CI run 37583360621 on commit 1942dc5, artifact
`production-shell-ui-html`; every job of the run passed.

| File | What it shows |
| --- | --- |
| `html-position-light.json` | The light `html-position` pass: its phases, including `position-text-size` (`htmlTextSizeMarks` 420 after each of four bursts, `htmlTextSizeSaves` 0) and `position-text-size-fallback`. |
| `html-position-dark.json` | The same pass in dark. |
| `html-position-light.html-place-shifted.png` | Picture Web Guide at 110% with `Text size 110%. Your place may have shifted.`, light. |
| `html-position-dark.html-place-shifted.png` | The same, dark. |

See the [design's verification](../../t14-3-appearance-restore-design.md#verification).
