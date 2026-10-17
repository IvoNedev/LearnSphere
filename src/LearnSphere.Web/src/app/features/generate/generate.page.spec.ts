import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject } from 'rxjs';
import { GeneratePage } from './generate.page';
import { GenerationsApi } from '../../core/generations-api.service';
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
    f.componentInstance.form.setValue({ title: 'Test', transcript: 'x'.repeat(600) });
    f.componentInstance.submit();
    f.componentInstance.submit();
    f.detectChanges();
    expect(calls).toBe(1);
    expect(f.componentInstance.pending()).toBe(true);
    expect(f.nativeElement.querySelector('button[type=submit]').disabled).toBe(true);
    f.destroy();
  });
});
