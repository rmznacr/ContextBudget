import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import App from './App'

afterEach(()=>{cleanup();vi.unstubAllGlobals()})
describe('Dashboard',()=>{
  it('shows a clear empty state when the API has no projects or traces',async()=>{
    vi.stubGlobal('fetch',vi.fn(async(input:RequestInfo|URL)=>{
      const url=String(input)
      const data=url.endsWith('/projects')?[]:{projectCount:0,traceCount:0,estimatedInputTokens:null,providerInputTokens:null,recentTraces:[]}
      return new Response(JSON.stringify(data),{status:200,headers:{'content-type':'application/json'}})
    }))
    render(<QueryClientProvider client={new QueryClient({defaultOptions:{queries:{retry:false}}})}><App/></QueryClientProvider>)
    expect(await screen.findByText('Henüz kullanım verisi yok')).toBeInTheDocument()
    expect(screen.getByText('Analiz geçmişi boş')).toBeInTheDocument()
  })
})

describe('Provider settings',()=>{
  it('does not call the model until the user explicitly tests the connection',async()=>{
    const fetchMock=vi.fn(async(input:RequestInfo|URL,init?:RequestInit)=>{
      const url=String(input)
      if(url.endsWith('/providers/config'))return new Response(JSON.stringify({provider:'EVREN Gateway',baseUrl:'https://evren-llmapi.ssyz.org.tr/v1',model:'deepseek-v4.1-flash',apiKeyConfigured:true,enabled:true}),{status:200})
      if(url.endsWith('/providers/test'))return new Response(JSON.stringify({provider:'EVREN Gateway',model:'deepseek-v4.1-flash',durationMs:42,usage:{inputTokens:8,outputTokens:1,cachedTokens:null},status:'SUCCESS',error:null}),{status:200})
      if(url.endsWith('/projects'))return new Response('[]',{status:200})
      if(url.endsWith('/dashboard'))return new Response(JSON.stringify({projectCount:0,traceCount:0,estimatedInputTokens:null,providerInputTokens:null,recentTraces:[]}),{status:200})
      throw new Error(`Unexpected request ${url} ${init?.method??''}`)
    })
    vi.stubGlobal('fetch',fetchMock)
    render(<QueryClientProvider client={new QueryClient({defaultOptions:{queries:{retry:false}}})}><App/></QueryClientProvider>)
    fireEvent.click(await screen.findByText('Ayarlar'))
    expect(await screen.findByText('EVREN Gateway')).toBeInTheDocument()
    expect(screen.getByText(/token tüketebilir/)).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url,init])=>String(url).endsWith('/providers/test')&&init?.method==='POST')).toBe(false)
    fireEvent.click(screen.getByRole('button',{name:'Test Connection'}))
    expect(await screen.findByText(/Bağlantı başarılı/)).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url,init])=>String(url).endsWith('/providers/test')&&init?.method==='POST')).toBe(true)
  })
})
