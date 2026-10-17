import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  catchError,
  combineLatest,
  EMPTY,
  map,
  startWith,
  Subject,
  switchMap,
  tap,
  timer,
} from 'rxjs';
import { GenerationsApi, pollGeneration } from '../../core/generations-api.service';
import { Generation } from '../../core/models';
import { problemDetails } from '../../core/problem-details';
import { QuizComponent } from './quiz.component';
@Component({
  selector: 'app-generation',
  imports: [RouterLink, QuizComponent, DatePipe, DecimalPipe],
  templateUrl: './generation.page.html',
})
export class GenerationPage {
  readonly api = inject(GenerationsApi);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private destroy = inject(DestroyRef);
  private refresh = new Subject<void>();
  readonly job = signal<Generation | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly elapsed = signal(0);
  readonly id = signal('');
  readonly attempts = signal(0);
  readonly maxAttempts = signal(0);
  readonly pollingStopped = signal(false);
  constructor() {
    combineLatest([
      this.route.paramMap.pipe(map((params) => params.get('id')!)),
      this.refresh.pipe(startWith(undefined)),
      this.api.capabilities(),
    ])
      .pipe(
        switchMap(([id, , caps]) => {
          this.id.set(id);
          this.job.set(null);
          this.error.set('');
          this.busy.set(false);
          this.elapsed.set(0);
          this.attempts.set(0);
          this.maxAttempts.set(caps.maxPollAttempts);
          this.pollingStopped.set(false);
          return pollGeneration(
            () => this.api.get(id),
            caps.pollIntervalMs,
            caps.maxPollAttempts,
            caps.pollRequestTimeoutMs,
            (attempt) => this.attempts.set(attempt),
          ).pipe(
            tap({
              next: (job) => this.job.set(job),
              complete: () => {
                if (this.active()) {
                  this.pollingStopped.set(true);
                  this.error.set(
                    'Status checking reached its limit. Your job was not restarted. Check status again when ready.',
                  );
                }
              },
            }),
            catchError((error) => {
              this.pollingStopped.set(true);
              this.error.set(
                error?.name === 'TimeoutError'
                  ? 'The status request timed out. The job continues on the server.'
                  : problemDetails(error).message,
              );
              return EMPTY;
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe({
        error: () => this.error.set('Could not load server settings. Reload the page to retry.'),
      });
    timer(0, 1000)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        const job = this.job();
        if (job && this.active())
          this.elapsed.set(
            Math.max(
              0,
              Math.floor((Date.now() - Date.parse(job.startedAt || job.createdAt)) / 1000),
            ),
          );
      });
  }
  active() {
    return this.job()?.status === 'Queued' || this.job()?.status === 'Processing';
  }
  checkStatus() {
    this.refresh.next();
  }
  retry() {
    if (this.busy()) return;
    const id = this.id();
    if (this.job()?.error?.resumeStage === 'PersistResult') {
      this.busy.set(true);
      this.api
        .resumePersistence(id)
        .pipe(takeUntilDestroyed(this.destroy))
        .subscribe({
          next: () => {
            if (this.id() === id) this.refresh.next();
          },
          error: (e) => {
            if (this.id() === id) {
              this.busy.set(false);
              this.error.set(problemDetails(e).message);
            }
          },
        });
      return;
    }
    const draft = this.api.drafts.get(id);
    if (!draft) {
      void this.router.navigate(['/']);
      return;
    }
    this.busy.set(true);
    this.api
      .create(draft)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (result) => {
          this.api.drafts.set(result.id, draft);
          if (this.id() === id) void this.router.navigate(['/generations', result.id]);
        },
        error: (e) => {
          if (this.id() === id) {
            this.busy.set(false);
            this.error.set(problemDetails(e).message);
          }
        },
      });
  }
  delete() {
    if (this.busy()) return;
    const id = this.id();
    this.busy.set(true);
    this.api
      .delete(id)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: () => {
          this.api.drafts.delete(id);
          if (this.id() === id) void this.router.navigate(['/']);
        },
        error: (e) => {
          if (this.id() === id) {
            this.busy.set(false);
            this.error.set(problemDetails(e).message);
          }
        },
      });
  }
}
