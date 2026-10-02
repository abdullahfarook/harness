using System.ComponentModel;
using Harness.Models;
using Microsoft.Playwright;

namespace Harness.Browser;

public sealed record PageLink(string Id, string Text, string Url);
public sealed record PageObservation(string Url, string Title, string Text, bool Truncated, IReadOnlyList<PageLink> Links);

public sealed class BrowserTools : IAsyncDisposable
{
    private readonly Uri target;
    private readonly UrlPolicy policy;
    private readonly EvidenceWriter evidence;
    private readonly LayaDecisionModel? laya;
    private IPlaywright? playwright;
    private IBrowser? browser;
    private IPage? page;
    private readonly HashSet<string> visited = [];
    private readonly Dictionary<string,PageLink> links = [];
    private int actions,observations;
    private float mouseX,mouseY;
    public PageObservation? LastObservation { get; private set; }
    public int Classifications { get; private set; }
    public IReadOnlyCollection<string> Visited => visited;
    public BrowserTools(Uri target, EvidenceWriter evidence, LayaDecisionModel? laya, UrlPolicy? policy = null)
    { this.target=target; this.evidence=evidence; this.laya=laya; this.policy=policy ?? new(target); }

    public async Task StartAsync(bool headless, CancellationToken token)
    {
        await policy.ValidateAsync(target.AbsoluteUri,true);
        token.ThrowIfCancellationRequested();
        playwright=await Playwright.CreateAsync();
        browser=await playwright.Chromium.LaunchAsync(new() { Headless=headless });
        IBrowserContext context=await browser.NewContextAsync(new() { ViewportSize=new() { Width=1280,Height=900 },ServiceWorkers=ServiceWorkerPolicy.Block,AcceptDownloads=false });
        await context.RouteWebSocketAsync("**/*",socket=> { evidence.Event("blocked_websocket",new { url=socket.Url }); _=socket.CloseAsync(new() { Code=1008,Reason="Read-only browsing blocks WebSockets" }); });
        await context.RouteAsync("**/*",async route =>
        {
            try { UrlPolicy.ValidateMethod(route.Request.Method); await FetchValidatedAsync(route); }
            catch (Exception exception) { evidence.Event("blocked_request",new { url=route.Request.Url,error=exception.Message }); await route.AbortAsync(); }
        });
        await context.AddInitScriptAsync("""
            addEventListener('DOMContentLoaded', () => {
              const cursor=document.createElement('div'); cursor.id='website-bot-cursor';
              cursor.style.cssText='position:fixed;left:0;top:0;width:18px;height:18px;background:#ff3355;border:3px solid white;border-radius:50%;pointer-events:none;z-index:2147483647;box-shadow:0 0 0 2px #222';
              document.documentElement.appendChild(cursor);
              addEventListener('mousemove',e=>{cursor.style.left=(e.clientX-9)+'px';cursor.style.top=(e.clientY-9)+'px';cursor.dataset.moves=String(Number(cursor.dataset.moves||0)+1);});
            });
            """);
        page=await context.NewPageAsync();
        if (!headless) { await page.BringToFrontAsync(); }
        page.SetDefaultTimeout(15000);
        page.SetDefaultNavigationTimeout(30000);
    }
    private async Task FetchValidatedAsync(IRoute route)
    {
        string url=route.Request.Url;
            await policy.ValidateAsync(url,route.Request.IsNavigationRequest);
            evidence.Event("request_fetch",new { url });
            IAPIResponse response=await route.FetchAsync(new() { Url=url,MaxRedirects=0,Timeout=15000 });
            evidence.Event("request_response",new { url=response.Url,status=response.Status });
            try
            {
                if (response.Status>=300 && response.Status<400 && response.Headers.TryGetValue("location",out string? location))
                { string destination=new Uri(new Uri(url),location).AbsoluteUri; await policy.ValidateAsync(destination,route.Request.IsNavigationRequest); throw new InvalidOperationException("Redirects are blocked; use the final public URL directly."); }
                await route.FulfillAsync(new() { Response=response }); evidence.Event("request_fulfilled",new { url=response.Url }); return;
            }
            finally { await response.DisposeAsync(); }
    }
    private void CountAction(CancellationToken token)
    { token.ThrowIfCancellationRequested(); if (++actions>20) { throw new InvalidOperationException("20-action browser budget exhausted."); } }

    [Description("Open the target website or another page on the same origin; returns observed text and clickable link IDs.")]
    public async Task<PageObservation> NavigateAsync([Description("Absolute target-origin HTTP(S) URL")] string url, CancellationToken token = default)
    {
        CountAction(token);
        Uri uri=await policy.ValidateAsync(url,true);
        if (!visited.Contains(uri.AbsoluteUri) && visited.Count>=5) { throw new InvalidOperationException("5-page budget exhausted."); }
        evidence.Event("tool_start",new { tool="navigate",url });
        IResponse? response=await page!.GotoAsync(uri.AbsoluteUri,new() { WaitUntil=WaitUntilState.DOMContentLoaded }).WaitAsync(token);
        if (response is null || !response.Ok) { throw new InvalidOperationException($"Navigation failed: {response?.Status}"); }
        await policy.ValidateAsync(page.Url,true);
        await MoveCursorAsync(150,120,20,token);
        return await CaptureAsync(token);
    }
    [Description("Read the current visible website text and available link IDs.")]
    public async Task<PageObservation> ObserveAsync(CancellationToken token = default)
    { CountAction(token); return await CaptureAsync(token); }
    private async Task<PageObservation> CaptureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await policy.ValidateAsync(page!.Url,true);
        if (!visited.Contains(page.Url) && visited.Count>=5) { throw new InvalidOperationException("5-page budget exhausted."); }
        visited.Add(page.Url);
        string text=await page.Locator("body").InnerTextAsync();
        if (string.IsNullOrWhiteSpace(text)) { throw new InvalidDataException("Website returned no visible text."); }
        links.Clear();
        ILocator anchors=page.Locator("a[href]");
        int count=Math.Min(await anchors.CountAsync(),100);
        for (int i=0;i<count;i++)
        {
            ILocator anchor=anchors.Nth(i);
            if (!await anchor.IsVisibleAsync()) { continue; }
            string? href=await anchor.GetAttributeAsync("href");
            if (!Uri.TryCreate(new Uri(page.Url),href,out Uri? uri) || uri.Scheme!=target.Scheme || uri.Host!=target.Host || uri.Port!=target.Port) { continue; }
            string id=$"link-{observations}-{i}";
            string label=(await anchor.InnerTextAsync()).Trim();
            if (label.Length==0) { continue; }
            await anchor.EvaluateAsync("(a,id)=>a.dataset.websiteBotLink=id",id);
            links[id]=new(id,label[..Math.Min(100,label.Length)],uri.AbsoluteUri);
        }
        LastObservation=new(page.Url,await page.TitleAsync(),text[..Math.Min(9000,text.Length)],text.Length>9000,links.Values.Take(30).ToArray());
        evidence.Write($"page-{observations:D2}.json",LastObservation);
        await page.ScreenshotAsync(new() { Path=Path.Combine(evidence.DirectoryPath,$"page-{observations:D2}.png") });
        observations++;
        string cursor=await page.EvaluateAsync<string>("()=>{const c=document.getElementById('website-bot-cursor');return JSON.stringify(c?{left:c.style.left,top:c.style.top,moves:Number(c.dataset.moves||0)}:null);}");
        evidence.Event("tool_result",new { tool="observe",page=LastObservation,cursor });
        return LastObservation;
    }
    [Description("Move the visible mouse cursor to an observed link and click it; returns the new page text.")]
    public async Task<PageObservation> ClickAsync([Description("Exact ID from the last observation's Links list")] string observedLinkId, CancellationToken token = default)
    {
        CountAction(token);
        if (!links.TryGetValue(observedLinkId,out PageLink? link)) { throw new InvalidOperationException("Unknown or stale observed link ID."); }
        await policy.ValidateAsync(link.Url,true);
        if (!visited.Contains(link.Url) && visited.Count>=5) { throw new InvalidOperationException("5-page budget exhausted."); }
        ILocator anchor=page!.Locator($"a[data-website-bot-link='{observedLinkId}']");
        await anchor.ScrollIntoViewIfNeededAsync();
        await anchor.EvaluateAsync("a=>a.removeAttribute('target')");
        LocatorBoundingBoxResult box=await anchor.BoundingBoxAsync() ?? throw new InvalidOperationException("Link has no visible bounds.");
        evidence.Event("tool_start",new { tool="click",observedLinkId,link.Url });
        await page.BringToFrontAsync();
        await MoveCursorAsync(box.X+box.Width/2,box.Y+box.Height/2,35,token);
        Task navigation=page.WaitForURLAsync(link.Url,new() { WaitUntil=WaitUntilState.DOMContentLoaded });
        await page.Mouse.ClickAsync(box.X+box.Width/2,box.Y+box.Height/2);
        evidence.Event("mouse_click",new { target=link.Url,currentUrl=page.Url });
        await navigation.WaitAsync(token);
        return await CaptureAsync(token);
    }
    [Description("Scroll the current page using the visible browser mouse.")]
    public async Task<PageObservation> ScrollAsync(int pixels, CancellationToken token = default)
    {
        CountAction(token);
        await MoveCursorAsync(900,650,20,token);
        await page!.Mouse.WheelAsync(0,Math.Clamp(pixels,-900,900));
        evidence.Event("tool_start",new { tool="scroll",pixels });
        return await CaptureAsync(token);
    }
    private async Task MoveCursorAsync(float x,float y,int steps,CancellationToken token)
    {
        float fromX=mouseX,fromY=mouseY;
        for (int i=1;i<=steps;i++) { token.ThrowIfCancellationRequested(); await page!.Mouse.MoveAsync(fromX+(x-fromX)*i/steps,fromY+(y-fromY)*i/steps); await Task.Delay(15,token); }
        mouseX=x; mouseY=y;
    }
    [Description("Ask the fast local Laya ONNX model whether the currently observed page describes the site's product or service. Returns calibrated typed probabilities.")]
    public DecisionResult Classify()
    {
        if (LastObservation is null || laya is null) { throw new InvalidOperationException("Observe a page before classifying."); }
        if (++Classifications>10) { throw new InvalidOperationException("Classification budget exhausted."); }
        DecisionResult result=laya.Decide(LastObservation.Title+"\n"+LastObservation.Text[..Math.Min(2500,LastObservation.Text.Length)],new Dictionary<string,DecisionQuestion> { ["relevant"] = new("noul","Does this page describe the site's products, services or capabilities?",new Dictionary<string,string>()) });
        evidence.Event("laya_result",new { model="convaiinnovations/laya (receptron ONNX export)",url=LastObservation.Url,result });
        return result;
    }
    public async ValueTask DisposeAsync() { if (browser is not null) { await browser.CloseAsync(); } playwright?.Dispose(); }
}
