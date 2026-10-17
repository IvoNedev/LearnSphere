import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer } from 'rxjs';
import { GenerationsApi, pollGeneration } from '../../core/generations-api.service';
import { Generation } from '../../core/models';
import { problemDetails } from '../../core/problem-details';
import { QuizComponent } from './quiz.component';
@Component({ selector: 'app-generation', imports: [RouterLink, QuizComponent, DatePipe, DecimalPipe], templateUrl: './generation.page.html' })
export class GenerationPage {
  readonly api = inject(GenerationsApi); private route = inject(ActivatedRoute); private router = inject(Router); private destroy = inject(DestroyRef);
  readonly job = signal<Generation | null>(null); readonly error = signal(''); readonly busy = signal(false); readonly elapsed = signal(0);
  readonly id = this.route.snapshot.paramMap.get('id')!;
  constructor() {
    pollGeneration(() => this.api.get(this.id)).pipe(takeUntilDestroyed()).subscribe({ next: j => this.job.set(j), error: e => this.error.set(problemDetails(e).message) });
    timer(0, 1000).pipe(takeUntilDestroyed()).subscribe(() => { const job = this.job(); if (job && this.active()) this.elapsed.set(Math.max(0, Math.floor((Date.now() - Date.parse(job.startedAt || job.createdAt)) / 1000))); });
  }
  active() { return this.job()?.status === 'Queued' || this.job()?.status === 'Processing'; }
  retry() {
    if (this.busy()) return;
    const draft = this.api.drafts.get(this.id);
    if (!draft) { void this.router.navigate(['/']); return; }
    this.busy.set(true);
    this.api.create(draft).pipe(takeUntilDestroyed(this.destroy)).subscribe({ next: result => { this.api.drafts.set(result.id, draft); void this.router.navigateByUrl('/', { skipLocationChange: true }).then(() => this.router.navigate(['/generations', result.id])); }, error: e => { this.busy.set(false); this.error.set(problemDetails(e).message); } });
  }
  delete() {
    if (this.busy()) return; this.busy.set(true);
    this.api.delete(this.id).pipe(takeUntilDestroyed(this.destroy)).subscribe({ next: () => { this.api.drafts.delete(this.id); void this.router.navigate(['/']); }, error: e => { this.busy.set(false); this.error.set(problemDetails(e).message); } });
  }
}
