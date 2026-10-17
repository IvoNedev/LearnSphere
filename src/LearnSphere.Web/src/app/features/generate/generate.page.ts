import { Component, DestroyRef, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { DatePipe, DecimalPipe } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { GenerationsApi } from '../../core/generations-api.service';
import { ListItem } from '../../core/models';
import { problemDetails } from '../../core/problem-details';

@Component({
  selector: 'app-generate',
  imports: [ReactiveFormsModule, RouterLink, DatePipe, DecimalPipe],
  templateUrl: './generate.page.html',
})
export class GeneratePage {
  readonly api = inject(GenerationsApi);
  private router = inject(Router);
  private destroy = inject(DestroyRef);
  private fb = inject(FormBuilder);
  readonly form = this.fb.nonNullable.group({
    title: ['', Validators.maxLength(200)],
    transcript: [
      '',
      [Validators.required, Validators.minLength(500), Validators.maxLength(200000)],
    ],
  });
  readonly pending = signal(false);
  readonly error = signal('');
  readonly history = signal<ListItem[]>([]);
  readonly count = signal(0);
  constructor() {
    this.api
      .list()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: (items) => this.history.set(items),
        error: (e) => this.error.set(problemDetails(e).message),
      });
    this.form.controls.transcript.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((value) => this.count.set(value.length));
  }
  loadSample() {
    this.api
      .sample()
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (transcript) => this.form.setValue({ title: 'Data privacy essentials', transcript }),
        error: (e) => this.error.set(problemDetails(e).message),
      });
  }
  submit() {
    if (this.pending() || this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.error.set('');
    const request = this.form.getRawValue();
    this.api
      .create(request)
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe({
        next: (result) => {
          this.api.drafts.set(result.id, request);
          this.pending.set(false);
          void this.router.navigate(['/generations', result.id]);
        },
        error: (e) => {
          this.pending.set(false);
          const problem = problemDetails(e);
          this.error.set(problem.message);
          for (const [field, messages] of Object.entries(problem.fields)) {
            if (field === 'title' || field === 'transcript')
              this.form.controls[field].setErrors({ server: messages.join(' ') });
          }
        },
      });
  }
}
