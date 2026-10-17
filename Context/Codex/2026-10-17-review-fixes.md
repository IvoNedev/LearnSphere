# Review fixes, bounded polling, recovery, and optional sections

Date: 2026-10-17 (Europe/Kiev)

## User request

IDE context: active AGENTS.md; open tabs included both revision-two plan paths and the brief.

```text
Fix #2 and make it output the correct error for every issue
Fix #3,4 (put a counter so we're not in a deadloop), 5, 6 (make it update the status accordingly, so we can retry the failed process, not the whole thing), 7 (make them both use the same limits - same origin data, not just equal but hardcoded values)

Explain some more, what do you mean "Confirm acceptance of sanitized logs with the evaluator; rotate the exposed key before publication"

Hmmm, i like the cap but also i like the ability to chunk large requests
We'll deal with github at the end, im the owner "Ivo Nedev"
```

## Assistant progress messages

I’ll fix provider-error reporting, preserve provider intent for queued jobs, bound polling, handle route changes, recover failed persistence without rerunning generation, and make Angular read its limits from the API.

“Sanitized logs” means the repository copies redact the key you pasted and omit internal reasoning. The brief asks for “unedited” logs, so I meant checking whether the reviewer accepts those disclosed exceptions—not publishing the secret. Rotating the key means revoking it in Google AI Studio and creating a replacement, since the original remains in chat history.

I’ll keep the 200k-character cap and assess chunking as an optional path within it. GitHub stays deferred; I’ve noted you as the owner, Ivo Nedev.

For #6, I’ll make Retry resume the save step using a recoverable copy of the generated result, without another Gemini call. While storage is unavailable, the app will report that failure rather than claim the save succeeded.

For chunking, I’ll add an optional “Process in sections” path within the 200k cap. The API will publish the input limits and polling settings to Angular, so there’s one source for both validation and display. Polling will show its attempt count and stop at a configured maximum.

The first pass is in place: token-count failures retain their error categories, queued jobs check their original provider/model, and Angular reads its limits from the API. Polling now waits for each response, counts attempts, and can be restarted manually without resubmitting the job.

The result-save checkpoint and optional section-processing path are also written. I’m now testing storage failures, route changes, slow polling, and chunk failures to verify the recovery behavior.

The new regression tests pass. They verify that provider failures don’t become size errors, a provider/model change stops queued work before inference, slow polling completes and stops at its attempt limit, route changes retarget the current job, and Retry can save a checkpointed result without another AI call.

Optional section processing is also tested for bounded splitting, source-excerpt validation, and failure without publishing partial content. I’m finishing Docker verification, including the upgrade of the existing data volume.

The live section-processing run succeeded: four sections and one synthesis call produced five questions, with every excerpt matching the source. It took about 74 seconds versus about 8 seconds for the earlier single-call run, so I’ll keep it optional.

Docker also preserved the existing history after the schema upgrade. The save-recovery tests confirm that a checkpoint can be reopened and saved on the same job ID with exactly one AI call across the original attempt and Retry.

Verification is complete: 96 backend tests and 16 Angular tests pass, and the API builds without warnings. Fault-injection tests also cover a read-only database and an unwritable checkpoint directory; once storage is restored, the save step succeeds without another provider call.

I’m recording the fixes and updating the running Docker app. GitHub publication remains deferred, and the credential is excluded from repository files.

## Actions and evidence (summary)

Implemented R2–R7 and optional bounded section processing in the current workspace. Added provider intents and an additive table initialization for existing SQLite volumes; no history reset. Added result-only save recovery under ignored App_Data, a persistence-only retry endpoint, shared capabilities, finite non-overlapping polling, reactive route loading, and quiz-state reset. Updated README and historical review remediation status. No account or repository publication actions occurred. The supplied credential was used transiently server-side for a synthetic live check and was not stored in project files.

96 backend and 16 Angular tests passed; warning-free API build and production Angular build passed. Verified Docker health, old history surviving the additive schema upgrade, and a four-section mock flow. Live Gemini section processing completed on the 46,152-character fixture in 73,855 ms, with four section-note calls plus final synthesis, 11,359 input/3,432 output/5,739 thought tokens reported in aggregate, no schema repair, and all five final excerpts matching the source. Results and a factual review are in samples/recorded-gemini-chunked-run.json. The source repeats modules and is not a broad course-quality evaluation. Source whitespace checks passed; transcript-rendered tool output can retain trailing whitespace. No connected browser visual QA was performed.

## Assistant final reply

Fixed #2–#7 and added optional chunking within the **200,000-character cap**.

- Provider failures retain their specific errors.
- Queued jobs cannot silently switch provider/model.
- Polling shows a counter, waits for responses, and stops at its limit.
- Navigation and quiz state follow the current job.
- **Retry saving result** resumes persistence on the same job without another AI call.
- Angular gets its limits from the API—one source of truth.

Passed **96 backend and 16 Angular tests**. Docker is running at http://localhost:8080. Live chunking passed, but took ~74 seconds versus ~8 seconds for one call, so it remains optional. Completed section notes are not checkpointed yet; save-stage results are.

By “evaluator,” I meant the hiring reviewer who supplied the brief. Their “unedited logs” requirement conflicts with publishing a pasted credential, so we disclose the redaction. The key has not been published. **Rotate** means revoke it in AI Studio, create a replacement, and update backend configuration without posting the replacement in chat.

GitHub remains deferred; owner noted as **Ivo Nedev**. Details are in the README.
