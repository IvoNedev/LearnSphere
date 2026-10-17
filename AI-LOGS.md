# AI conversation archive and disclosed exceptions

The brief requests complete unedited assistant conversations. The available project sessions are indexed in [Context/README.md](Context/README.md): Codex and Claude Markdown renderings plus JSONL publication copies under each `raw/` directory. Handwritten Codex notes are supplemental and do not replace these records.

Publication copies preserve available user-facing messages and tool activity, with these explicit exceptions:

- A Gemini credential was pasted in chat on 2026-10-17. Credential-shaped strings are replaced with `[REDACTED_CREDENTIAL]`, including inside tool results and nested exported text. Original credentials are not committed. Rotate the supplied key before submission.
- Internal reasoning/thinking records, assistant analysis messages, and encrypted reasoning are omitted. They are not user-facing conversation content.
- JSONL is reserialized, and incomplete trailing records in a currently active session are deferred until the next sync. Markdown is a readable rendering, not a byte-for-byte export.

Therefore these are **sanitized available conversation exports**, not literally unedited original logs. Original tool session files outside the repository are left untouched. This exception should be disclosed to the evaluator rather than representing sanitized material as unedited.

The existing `.claude/hooks/save-transcript.js` archives sessions whose workspace matches this project. It runs on Claude Stop/SessionEnd; Codex execution also invokes it explicitly before a delivery commit. `scripts/archive-safety.cjs` sanitizes before writing repository copies. Its tests verify credential redaction and omission of internal records. Refresh after the latest completed assistant turn:

```sh
node .claude/hooks/save-transcript.js
node --test scripts/archive-safety.test.cjs
```

The transcript of an active turn can lag its final reply. Run the sync once more after that turn finishes, then inspect coverage and the diff before publication. The hook cannot retrieve unavailable web chats or sessions outside the matched local stores. Additional assistants require their own exports. Sanitization is best effort; review all publication artifacts for secrets before pushing. Nothing has been published by this implementation task.
