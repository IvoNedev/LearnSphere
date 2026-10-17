import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { GeneratePage } from './generate.page';
import { GenerationsApi } from '../../core/generations-api.service';
import { TEST_CAPS } from '../../core/test-capabilities';
describe('GeneratePage', () => {
  it('prevents duplicate submissions while pending', () => {
    const response = new Subject<{ id: string; status: string }>();
    let calls = 0;
    TestBed.configureTestingModule({
      imports: [GeneratePage],
      providers: [
        provideRouter([]),
        {
          provide: GenerationsApi,
          useValue: {
            list: () => of([]),
            capabilities: () => of(TEST_CAPS),
            drafts: new Map(),
            create: () => {
              calls++;
              return response;
            },
          },
        },
      ],
    });
    const f = TestBed.createComponent(GeneratePage);
    f.componentInstance.form.setValue({
      title: 'Test',
      transcript: 'x'.repeat(600),
      useChunking: false,
    });
    f.componentInstance.submit();
    f.componentInstance.submit();
    f.detectChanges();
    expect(calls).toBe(1);
    expect(f.componentInstance.pending()).toBe(true);
    expect(f.nativeElement.querySelector('button[type=submit]').disabled).toBe(true);
    f.destroy();
  });
  it('uses server-provided limits for validators and displayed range', () => {
    TestBed.configureTestingModule({
      imports: [GeneratePage],
      providers: [
        provideRouter([]),
        {
          provide: GenerationsApi,
          useValue: { list: () => of([]), capabilities: () => of(TEST_CAPS), drafts: new Map() },
        },
      ],
    });
    const f = TestBed.createComponent(GeneratePage);
    f.detectChanges();
    f.componentInstance.form.patchValue({ transcript: 'x'.repeat(25) });
    expect(f.componentInstance.form.valid).toBe(true);
    f.componentInstance.form.patchValue({ transcript: 'x'.repeat(1001) });
    expect(f.componentInstance.form.invalid).toBe(true);
    expect(f.nativeElement.textContent).toContain('20–1,000');
    f.destroy();
  });
});
