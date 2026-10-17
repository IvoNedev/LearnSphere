import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { defer, exhaustMap, Observable, shareReplay, take, takeWhile, timeout, timer } from 'rxjs';
import { Capabilities, CreateRequest, Generation, ListItem } from './models';

export function pollGeneration(
  load: () => Observable<Generation>,
  interval: number,
  maxAttempts: number,
  timeoutMs: number,
  onAttempt: (attempt: number) => void = () => {},
) {
  let attempts = 0;
  return timer(0, interval).pipe(
    exhaustMap(() =>
      defer(() => {
        onAttempt(++attempts);
        return load().pipe(timeout(timeoutMs));
      }),
    ),
    takeWhile((job) => job.status === 'Queued' || job.status === 'Processing', true),
    take(maxAttempts),
  );
}
@Injectable({ providedIn: 'root' })
export class GenerationsApi {
  private http = inject(HttpClient);
  private base = '/api/v1/generations';
  private capabilities$ = this.http
    .get<Capabilities>('/api/v1/capabilities')
    .pipe(shareReplay({ bufferSize: 1, refCount: false }));
  capabilities() {
    return this.capabilities$;
  }
  // Only in browser memory. Never persisted to localStorage; lost on reload.
  readonly drafts = new Map<string, CreateRequest>();
  create(request: CreateRequest) {
    return this.http.post<{ id: string; status: string }>(this.base, request);
  }
  get(id: string) {
    return this.http.get<Generation>(`${this.base}/${encodeURIComponent(id)}`);
  }
  resumePersistence(id: string) {
    return this.http.post<{ id: string; status: string }>(
      `${this.base}/${encodeURIComponent(id)}/retry`,
      {},
    );
  }
  list() {
    return this.http.get<ListItem[]>(this.base);
  }
  delete(id: string) {
    return this.http.delete<void>(`${this.base}/${encodeURIComponent(id)}`);
  }
  sample() {
    return this.http.get('/samples/synthetic-long.txt', { responseType: 'text' });
  }
}
