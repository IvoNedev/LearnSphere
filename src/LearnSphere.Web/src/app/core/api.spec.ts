import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, toArray } from 'rxjs';
import { GenerationsApi, pollGeneration } from './generations-api.service';
import { Generation } from './models';
import { problemDetails } from './problem-details';
describe('API and polling',()=>{
 for(const terminal of ['Succeeded','Failed'] as const) it(`stops polling at ${terminal}`,async()=>{let calls=0;const result=await firstValueFrom(pollGeneration(()=>of({status:++calls===1?'Processing':terminal} as Generation),1).pipe(toArray()));expect(result.length).toBe(2);expect(calls).toBe(2);});
 it('maps server field errors',()=>{const p=problemDetails(new HttpErrorResponse({status:400,error:{title:'Invalid transcript',errors:{transcript:['Too short']}}}));expect(p.fields['transcript']).toEqual(['Too short']);expect(p.message).toBe('Invalid transcript');});
 it('uses same-origin API requests',()=>{TestBed.configureTestingModule({providers:[provideHttpClient(),provideHttpClientTesting()]});const api=TestBed.inject(GenerationsApi);const http=TestBed.inject(HttpTestingController);api.create({transcript:'source'}).subscribe();const r=http.expectOne('/api/v1/generations');expect(r.request.method).toBe('POST');r.flush({id:'1',status:'Queued'});http.verify();});
});
