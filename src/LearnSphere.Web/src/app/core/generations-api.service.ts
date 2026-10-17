import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, switchMap, takeWhile, timer } from 'rxjs';
import { CreateRequest, Generation, ListItem } from './models';

export function pollGeneration(load: () => Observable<Generation>, interval = 1500) {
  return timer(0, interval).pipe(switchMap(load), takeWhile(job => job.status === 'Queued' || job.status === 'Processing', true));
}
@Injectable({ providedIn: 'root' })
export class GenerationsApi {
  private http = inject(HttpClient);
  private base = '/api/v1/generations';
  // Only in browser memory. Never persisted to localStorage; lost on reload.
  readonly drafts = new Map<string, CreateRequest>();
  create(request: CreateRequest) { return this.http.post<{ id: string; status: string }>(this.base, request); }
  get(id: string) { return this.http.get<Generation>(`${this.base}/${encodeURIComponent(id)}`); }
  list() { return this.http.get<ListItem[]>(this.base); }
  delete(id: string) { return this.http.delete<void>(`${this.base}/${encodeURIComponent(id)}`); }
  sample() { return this.http.get('/samples/synthetic-long.txt', { responseType: 'text' }); }
}
