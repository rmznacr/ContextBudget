import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import Experiments from './Experiments'

afterEach(()=>{cleanup();vi.unstubAllGlobals()})
function mount(){render(<QueryClientProvider client={new QueryClient({defaultOptions:{queries:{retry:false}}})}><Experiments projects={[{id:'p1',name:'Project'}]}/></QueryClientProvider>)}

describe('Experiments',()=>{
  it('shows empty history and previews paired calls without starting a provider request',async()=>{
    const fetchMock=vi.fn(async(input:RequestInfo|URL)=>{const url=String(input);if(url.endsWith('/providers/config'))return new Response(JSON.stringify({provider:'fake',model:'fake-model',enabled:true}),{status:200});if(url.endsWith('/experiments'))return new Response('[]',{status:200});return new Response('[]',{status:200})})
    vi.stubGlobal('fetch',fetchMock);mount()
    expect(await screen.findByText(/Henüz deney yok/)).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Proje'),{target:{value:'p1'}})
    expect(document.querySelector('.experiment-actions')?.textContent).toContain('2 model çağrısı')
    expect(fetchMock.mock.calls.some(([url])=>String(url).includes('/runs'))).toBe(false)
  })

  it('reports provider unavailable and keeps run start disabled',async()=>{
    vi.stubGlobal('fetch',vi.fn(async(input:RequestInfo|URL)=>{const url=String(input);if(url.endsWith('/providers/config'))return new Response(JSON.stringify({provider:'fake',model:'fake-model',enabled:false}),{status:200});if(url.endsWith('/experiments'))return new Response('[]',{status:200});return new Response('[]',{status:200})}))
    mount();expect(await screen.findByText(/Provider kullanılamıyor/)).toBeInTheDocument();expect(screen.queryByRole('button',{name:/Start Experiment/})).not.toBeInTheDocument()
  })

  it('creates a dataset and experiment, then starts only on explicit click and shows a failed quality gate',async()=>{
    vi.stubGlobal('crypto',{randomUUID:()=> 'request-key'})
    const check={name:'RequiredFact',version:'deterministic-v1',result:'Failed',score:0,failureReason:'Required value not found.',severity:'critical'}
    const result={caseId:'case-1',caseName:'Örnek senaryo',repeat:1,variant:'B',status:'Succeeded',providerInputTokens:8,providerOutputTokens:2,cachedTokens:null,estimatedInputTokens:9,estimatedOutputTokens:2,latencyMs:20,checks:[check],failureCode:''}
    const run={id:'run-1',experimentId:'exp-1',status:'Completed',qualityGate:'REGRESSION_DETECTED',summary:{qualityGate:'REGRESSION_DETECTED',requestedCalls:2,completedCalls:2,successfulCalls:2,failedCalls:0,passedChecks:1,failedChecks:1,notEvaluatedChecks:0,regressions:1,baselineInputTokens:10,optimizedInputTokens:8,baselineOutputTokens:3,optimizedOutputTokens:2,inputTokenReductionPercent:20,inputTokenDifference:2,outputTokenDifference:1,totalTokenDifference:3,latencyDifferenceMs:2,baselineLatencyP50Ms:null,optimizedLatencyP50Ms:null,manualReviewCases:0,tokenMetric:'Provider-reported input/output tokens'},results:[{...result,variant:'A',checks:[{...check,result:'Passed',score:1}]},result]}
    let runStarted=false
    const fetchMock=vi.fn(async(input:RequestInfo|URL,init?:RequestInit)=>{
      const url=String(input);const method=init?.method??'GET'
      if(url.endsWith('/providers/config'))return new Response(JSON.stringify({provider:'fake',model:'fake-model',enabled:true}),{status:200})
      if(url.endsWith('/experiments')&&method==='GET')return new Response('[]',{status:200})
      if(url.includes('/datasets?'))return new Response('[]',{status:200})
      if(url.endsWith('/datasets')&&method==='POST')return new Response(JSON.stringify({id:'dataset-1',projectId:'p1',name:'test',versions:[{id:'version-1',version:1,caseCount:1,contentHash:'hash'}]}),{status:201})
      if(url.endsWith('/experiments')&&method==='POST')return new Response(JSON.stringify({id:'exp-1',projectId:'p1',datasetVersionId:'version-1',name:'Experiment',provider:'fake',model:'fake-model',repeats:1,status:'Pending',totalRunCount:0,totalExecutionCount:0,latestRunId:null,latestQualityGate:null}),{status:201})
      if(url.endsWith('/experiments/exp-1/runs')&&method==='POST'){runStarted=true;return new Response(JSON.stringify({...run,status:'Running',qualityGate:'EVALUATION_INCOMPLETE'}),{status:202})}
      if(url.endsWith('/experiments/runs/run-1/comparison'))return new Response(JSON.stringify({cost:{currency:null,baseline:{totalCalculatedCost:null,missingInputs:['pricing_profile']},optimized:{totalCalculatedCost:null,missingInputs:['pricing_profile']},absoluteCostDifference:null,qualityGate:'REGRESSION_DETECTED'},performance:{pairedCount:1,unpairedCount:0,pairedLatencyDifferenceMs:2,baseline:{p95LatencyMs:null},optimized:{p95LatencyMs:null},sampleWarning:'Small sample'}}),{status:200})
      if(url.endsWith('/experiments/runs/run-1'))return new Response(JSON.stringify(run),{status:200})
      if(url.endsWith('/experiments/exp-1/runs'))return new Response(JSON.stringify([run]),{status:200})
      throw new Error(`Unexpected ${method} ${url}`)
    })
    vi.stubGlobal('fetch',fetchMock);mount()
    fireEvent.change(screen.getByLabelText('Proje'),{target:{value:'p1'}})
    fireEvent.change(screen.getByLabelText('Deney adı'),{target:{value:'Experiment'}})
    fireEvent.change(screen.getByLabelText('Baseline context A'),{target:{value:'system A'}})
    fireEvent.change(screen.getByLabelText('Optimized context B'),{target:{value:'system B'}})
    fireEvent.click(screen.getByRole('button',{name:/Dataset metadata oluştur/}))
    expect(await screen.findByText(/Dataset sürümü seçildi/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button',{name:'Deney oluştur'}))
    expect(await screen.findByRole('button',{name:/Start Experiment/})).toBeInTheDocument()
    expect(runStarted).toBe(false)
    fireEvent.click(screen.getByRole('button',{name:/Start Experiment/}))
    expect(await screen.findByText('REGRESSION_DETECTED')).toBeInTheDocument()
    expect(await screen.findByText('20.0%')).toBeInTheDocument()
    expect(screen.getByText('A · OUTPUT')).toBeInTheDocument()
    expect(runStarted).toBe(true)
    fireEvent.click(screen.getByRole('button',{name:'Cost & Performance'}))
    expect(await screen.findByText('Run cost & performance')).toBeInTheDocument()
    expect(await screen.findByText(/Eksik fiyat\/kullanım A: pricing_profile/)).toBeInTheDocument()
  })
})
