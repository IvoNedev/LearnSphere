# Angular and candidate deviations

Date: 2026-10-17 (Europe/Kiev)

## User

```text
# Context from my IDE setup:

## Active file: Task/prescreen-brief-ivo-nedev.md

## Open tabs:
- prescreen-brief-ivo-nedev.md: Task/prescreen-brief-ivo-nedev.md
- prescreen-brief-ivo-nedev.md: prescreen-brief-ivo-nedev.md

## My request:
One note, I want to use Angular as the front-end framework. Before we proceed with step two suggest any diviations that we may want to explore ?
```

## Assistant progress update

> I’ll update the plan to use Angular and review which other changes would be worth considering before step two.

## Actions (summary)

Read the project instructions and current plan. Consulted Angular's official HttpClient setup documentation at https://angular.dev/guide/http/setup. Updated the OP plan to reflect Angular, its build/development setup and additional time, and recorded optional deviations separately from confirmed scope. No application code was implemented and step two was not invoked.

## Assistant final reply

Angular is now reflected in [the plan](/C:/Users/inede/Documents/repos/LearnSphere/Task/OP/prescreen-brief-ivo-nedev.md). I suggest standalone components, reactive forms, and a small typed API service using [Angular HttpClient](https://angular.dev/guide/http/setup). Allow roughly 30 extra minutes for frontend setup and build integration.

Before step two, I’d consider these deviations, in priority order:

1. **Demonstrate real AI generation.** Keep mock mode for easy setup, but aim to deliver one working live-provider integration plus a sample transcript. This best demonstrates the brief’s main requirement.
2. **Make answers reviewable.** Include a supporting transcript excerpt for each quiz answer. This helps an L&D manager check the output; it adds prompt, validation, and UI work.
3. **Reassess long-transcript support.** The proposed 40,000-character ceiling is our assumption. Test it against a representative course transcript before deciding whether chunking deserves the additional scope.
4. **Improve the demo workflow.** A “Load sample transcript” button and JSON download would make evaluation easier without requiring a database.

My recommendation: Angular plus a live integration and sample transcript as the baseline; supporting excerpts and download as stretch goals. Keep persistence, authentication, streaming, and background jobs deferred to protect the eight-hour budget.
