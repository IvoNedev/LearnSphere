import { TestBed } from '@angular/core/testing';
import { QuizComponent } from './quiz.component';
const question = {
  prompt: 'Which policy applies?',
  options: ['One', 'Two', 'Three', 'Four'],
  correctAnswerIndex: 1,
  explanation: 'Use the second policy.',
  sourceExcerpt: 'Second policy',
  excerptVerified: true,
};
describe('QuizComponent', () => {
  it('resets answers and revealed keys when the source questions change', () => {
    const f = TestBed.createComponent(QuizComponent);
    f.componentRef.setInput('questions', [question]);
    f.detectChanges();
    f.componentInstance.choose(0, 1);
    f.componentInstance.checked.set(true);
    f.componentInstance.showKey.set(true);
    f.detectChanges();
    f.componentRef.setInput('questions', [{ ...question, prompt: 'Another course' }]);
    f.detectChanges();
    expect(f.componentInstance.answers()).toEqual({});
    expect(f.componentInstance.checked()).toBe(false);
    expect(f.componentInstance.showKey()).toBe(false);
  });
  it('checks correct and incorrect choices and toggles evidence', () => {
    const fixture = TestBed.createComponent(QuizComponent);
    fixture.componentRef.setInput('questions', [question]);
    fixture.detectChanges();
    fixture.componentInstance.choose(0, 0);
    fixture.componentInstance.checked.set(true);
    fixture.detectChanges();
    expect(fixture.componentInstance.score()).toBe(0);
    expect(fixture.nativeElement.querySelector('.incorrect')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.correct')).toBeTruthy();
    fixture.componentInstance.choose(0, 1);
    fixture.componentInstance.checked.set(true);
    fixture.detectChanges();
    expect(fixture.componentInstance.score()).toBe(1);
    fixture.componentInstance.checked.set(false);
    fixture.componentInstance.showKey.set(true);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Excerpt found in source');
  });
  it('renders generated markup as text', () => {
    const f = TestBed.createComponent(QuizComponent);
    f.componentRef.setInput('questions', [{ ...question, prompt: '<script>unsafe()</script>' }]);
    f.detectChanges();
    expect(f.nativeElement.querySelector('script')).toBeNull();
    expect(f.nativeElement.textContent).toContain('<script>unsafe()</script>');
  });
});
