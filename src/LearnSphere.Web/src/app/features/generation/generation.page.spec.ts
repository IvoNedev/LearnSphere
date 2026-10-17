import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { GenerationsApi } from '../../core/generations-api.service';
import { Generation } from '../../core/models';
import { TEST_CAPS } from '../../core/test-capabilities';
import { GenerationPage } from './generation.page';

@Component({ template: '' })
class BlankPage {}
describe('GenerationPage route and recovery', () => {
  it('loads a changed route ID and directs Delete to the current job', async () => {
    const requested: string[] = [];
    const removed: string[] = [];
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: '', component: BlankPage },
          { path: 'generations/:id', component: GenerationPage },
        ]),
        {
          provide: GenerationsApi,
          useValue: {
            capabilities: () => of(TEST_CAPS),
            drafts: new Map(),
            get: (id: string) => {
              requested.push(id);
              return of({
                id,
                status: 'Succeeded',
                createdAt: new Date().toISOString(),
                generationMode: 'mock',
              } as Generation);
            },
            delete: (id: string) => {
              removed.push(id);
              return of(undefined);
            },
          },
        },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const a = await harness.navigateByUrl('/generations/A', GenerationPage);
    await vi.waitFor(() => expect(a.job()?.id).toBe('A'));
    const b = await harness.navigateByUrl('/generations/B', GenerationPage);
    await vi.waitFor(() => expect(b.job()?.id).toBe('B'));
    expect(b).toBe(a);
    expect(b.id()).toBe('B');
    b.delete();
    expect(removed).toEqual(['B']);
    expect(requested).toEqual(['A', 'B']);
  });
  it('retries persistence on the same ID without resubmitting source', async () => {
    let saved = false;
    let resumes = 0;
    const create = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'generations/:id', component: GenerationPage }]),
        {
          provide: GenerationsApi,
          useValue: {
            capabilities: () => of(TEST_CAPS),
            drafts: new Map(),
            create,
            get: (id: string) =>
              of({
                id,
                status: saved ? 'Succeeded' : 'Failed',
                generationMode: 'mock',
                createdAt: new Date().toISOString(),
                error: saved
                  ? undefined
                  : {
                      code: 'result_persistence_failed',
                      message: 'Save failed',
                      retryable: true,
                      resumeStage: 'PersistResult',
                    },
              } as Generation),
            resumePersistence: () => {
              resumes++;
              saved = true;
              return of({ id: 'A', status: 'Succeeded' });
            },
          },
        },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/generations/A', GenerationPage);
    await vi.waitFor(() => expect(page.job()?.status).toBe('Failed'));
    page.retry();
    await vi.waitFor(() => expect(page.job()?.status).toBe('Succeeded'));
    expect(resumes).toBe(1);
    expect(create).not.toHaveBeenCalled();
    expect(page.id()).toBe('A');
  });
  it('shows the counter limit and lets users check status without restarting generation', async () => {
    let calls = 0;
    const create = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'generations/:id', component: GenerationPage }]),
        {
          provide: GenerationsApi,
          useValue: {
            capabilities: () => of(TEST_CAPS),
            drafts: new Map(),
            create,
            get: (id: string) => {
              calls++;
              return of({
                id,
                status: 'Processing',
                generationMode: 'mock',
                createdAt: new Date().toISOString(),
              } as Generation);
            },
          },
        },
      ],
    });
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/generations/A', GenerationPage);
    await vi.waitFor(() => expect(page.pollingStopped()).toBe(true));
    expect(page.attempts()).toBe(3);
    expect(calls).toBe(3);
    page.checkStatus();
    await vi.waitFor(() => expect(calls).toBe(6));
    expect(create).not.toHaveBeenCalled();
  });
});
