# Forest Guard — external source review entry

Start here: `review/forest-guard/` on `fix/forest-guard`.

**Do not treat the inherited repository-root source as the installed version.** Git ancestry is the historical Dev commit `6deb6cae3ee464ff241ecdabd676effb13a18521`. The installed source commit is unknown; exact build manifests pin the review baseline below. This is a scoped source-review snapshot, not a complete build tree or a merge-ready replacement for Dev.

## Files

- `before/src-modern/`: five unchanged source files from the exact former installed Client7FA baseline.
- `after/src-modern/`: only the six accepted diagnostic files (five changed originals and one new helper).
- `forest-guard.patch`: text diff between those directories, including the new helper; for inspection, not blindly applying to stale Dev.
- `manifest.json`: exact original-byte SHA256 pins and baseline/successor identities. Nested attributes disable newline conversion so the copied source bytes stay pinned.

No runtime logs, saves, private configs, secrets, DLLs, native libraries, assets or unrelated gameplay changes were added. Dependencies outside this slice are deliberately not republished. Ask for a precise dependency if needed; do not infer its current implementation from stale root source.

## What changed / what did not

The delta adds diagnostic observations at lifecycle request/outcome, owner application, physical-position rejection, visibility staging and presentation gates. Predicate short-circuit order, once-only evaluation and existing gameplay decisions remain unchanged. The bounded logger separates first/latest decisive failures from routine traffic, so routine FIFO overflow cannot evict those causes.

Baseline: Client `7FA767D4E70477EDDC9A8CD7B2E1255EF25510E8641EA40FA16349FB2E42BF08`, source gate `D3756CAF2FBBD1295BCF6B0A0080D0AB5E987B571E91C7E113D789D35A7151B5`.
Accepted diagnostic manifest: `FAE084CE0642CFD26A0780C872F427FEDE358D841AC9DF843469E3DDD02AC05D`.
Root reports the diagnostic successor Client `42C54732409D5EA38228EA572B2B0E9D232331CCFAC07E877F0E761C5BAD72FD` installed for testing; dedicated Server `0CD3FD01CA71A268B5BB2D8BE2D56037932232073D7497BA41DB4D19C26BA02D` unchanged. The build's packaging/version stamp is outside this six-file mechanic slice. Wirev13 unchanged. This Git task performs no build/install.

## Existing evidence and open issue

Reused owner evidence:15 controlled diagnostic checks PASS, including a90-second routine-critical flood after allowance exhaustion; focused native Client API compile0errors (142warnings). Independent affected review verified five-file reversal of45 edits and safe callback/predicate boundaries. These are source/controlled/native evidence, **not a LIVE gameplay PASS or a proven spawn fix**. No tests were rerun merely for this publication.

The old local-host log reached existing resident UID/owner/appearance processing but exhausted its diagnostic allowance. It does not establish the final physical-spawn failure or a dedicated-server cause.

Next one manual action: reproduce for60–90seconds with a focused camera near the active Forest Ward on the declared role, retaining full local-session logs privately for the owner. Client-only diagnostics cannot inspect the unchanged dedicated server's internal failure gates. Do not publish raw logs or infer a lifecycle/AI/persistence rewrite before the actual failed gate is established.

External review request: inspect the exact before/after delta for diagnostic blind spots, predicate/callback changes, and bounded-log retention. Distinguish a concrete defect from a hypothesis; identify the smallest evidence needed. No new mechanics or UI redesign.
