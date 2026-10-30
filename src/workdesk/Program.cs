using System.Diagnostics;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.UseUrls("http://127.0.0.1:5190");
builder.Services.AddSingleton<Modules>();
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
var app = builder.Build();
app.Use(async (ctx, next) => {
    if (ctx.Request.Host.Host is not ("127.0.0.1" or "localhost")) { ctx.Response.StatusCode = 403; return; }
    if (ctx.Request.Method != "GET" && ctx.Request.Headers.Origin.Count > 0 && ctx.Request.Headers.Origin.ToString() != $"http://{ctx.Request.Host}") { ctx.Response.StatusCode = 403; return; }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/health", () => new { product = "WorkDesk", ok = true });
app.MapGet("/api/modules", async (Modules modules) => await modules.Status());
app.MapPost("/api/modules/{key}/start", async (string key, Modules modules) => {
    try { await modules.Ensure(key); return Results.Ok(new { ok = true }); }
    catch(Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapMethods("/m/{key}/{**path}", ["GET", "POST", "DELETE", "PUT"], async (string key, string? path, HttpContext ctx, Modules modules, HttpClient http) => {
    if (!modules.Items.TryGetValue(key, out var module)) { ctx.Response.StatusCode = 404; return; }
    try {
        await modules.Ensure(key);
        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), module.Url + (path ?? "") + ctx.Request.QueryString);
        if (ctx.Request.Method is not ("GET" or "HEAD")) { request.Content = new StreamContent(ctx.Request.Body); if(ctx.Request.ContentType != null) request.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType); request.Headers.TryAddWithoutValidation("Origin", module.Url.TrimEnd('/')); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)response.StatusCode;
        ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString();
        if(response.Headers.Location is { } location) ctx.Response.Headers.Location = $"/m/{key}/" + location.ToString().TrimStart('/');
        if (ctx.Response.ContentType?.Contains("text/html") == true) {
            var html = await response.Content.ReadAsStringAsync(ctx.RequestAborted);
            html = html.Replace("href=\"/", $"href=\"/m/{key}/").Replace("src=\"/", $"src=\"/m/{key}/");
            var hook = "<script>window.workdeskModule=" + JsonSerializer.Serialize(key) + ";const originalFetch=window.fetch.bind(window);window.fetch=(url,opts)=>originalFetch(typeof url==='string'&&url.startsWith('/api/')?'/m/'+window.workdeskModule+url:url,opts);</script>";
            html = html.Replace("<head>", "<head><base href=\"/m/"+key+"/\">"+hook).Replace("</body>", "<script src=\"/bridge.js\" defer></script></body>");
            await ctx.Response.WriteAsync(html);
        } else await response.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    } catch(Exception ex) { if(!ctx.Response.HasStarted) {ctx.Response.StatusCode=502; await ctx.Response.WriteAsJsonAsync(new {error=ex.Message});} }
});
var packetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkDesk", "materials");
Directory.CreateDirectory(packetRoot);
app.MapGet("/api/materials", () => Directory.EnumerateFiles(packetRoot, "*.json").Select(p => JsonSerializer.Deserialize<Material>(File.ReadAllText(p))).Where(p=>p!=null).OrderByDescending(p=>p!.CreatedAt));
app.MapPost("/api/materials", (MaterialInput input) => {
    if(string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Text) || input.Title.Length>200 || input.Text.Length>200000) return Results.BadRequest(new {error="请填写标题和内容；内容最多 20 万字。"});
    var material = new Material(Guid.NewGuid().ToString("N"), input.Title, input.Text, input.Source ?? "手动", DateTimeOffset.Now);
    var path = Path.Combine(packetRoot, material.Id+".json"); File.WriteAllText(path+".tmp", JsonSerializer.Serialize(material)); File.Move(path+".tmp",path,true);
    return Results.Ok(material);
});
app.MapDelete("/api/materials/{id}", (string id) => { if(!Guid.TryParseExact(id,"N",out _)) return Results.BadRequest(); var p=Path.Combine(packetRoot,id+".json"); if(!File.Exists(p)) return Results.NotFound(); File.Delete(p);return Results.NoContent(); });
app.MapPost("/api/guio/open", () => { var p=Process.Start(new ProcessStartInfo(@"C:\Users\Acer\GuIO\guio-app.exe") {WorkingDirectory=@"C:\Users\Acer\GuIO",UseShellExecute=true}); return Results.Ok(new {ok=p!=null}); });
app.MapPost("/api/guio/{command}", async (string command) => {
    if(command is not ("ping" or "devices")) return Results.BadRequest();
    try {
        using var p=new Process { StartInfo=new ProcessStartInfo(@"C:\Users\Acer\GuIO\cli\guio.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=@"C:\Users\Acer\GuIO"}};
        p.StartInfo.ArgumentList.Add(command);p.Start(); var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20)); try {await p.WaitForExitAsync(timeout.Token);} catch {if(!p.HasExited)p.Kill(true); throw new Exception("设备检查超时，请打开 GuIO 检查连接。");}
        return Results.Ok(new {ok=p.ExitCode==0,output=await output,error=await error});
    }catch(Exception ex){return Results.BadRequest(new {error=ex.Message});}
});
app.Run();
record MaterialInput(string Title,string Text,string? Source);
record Material(string Id,string Title,string Text,string Source,DateTimeOffset CreatedAt);
record Module(string Product,int Port,string Root) { public string Url=>$"http://127.0.0.1:{Port}/"; }
sealed class Modules {
    public Dictionary<string,Module> Items {get;}=new() {
        ["research"]=new("ResearchDesk",5182,@"E:\xiangmujihua\apps\ResearchDesk"),
        ["prompt"]=new("PromptLab",5181,@"E:\xiangmujihua\apps\PromptLab"),
        ["agent"]=new("AgentHub",5178,@"E:\xiangmujihua\apps\AgentHub")};
    readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(2)};
    readonly SemaphoreSlim gate=new(1,1);
    async Task<bool> Healthy(Module m) {try { var data=await client.GetFromJsonAsync<JsonElement>(m.Url+"api/health");return data.GetProperty("product").GetString()==m.Product;}catch{return false;}}
    public async Task<object[]> Status()=>await Task.WhenAll(Items.Select(async kv=>(object)new {key=kv.Key,name=kv.Value.Product,ready=await Healthy(kv.Value)}));
    public async Task Ensure(string key) {
        if(!Items.TryGetValue(key,out var m))throw new Exception("未知模块。");
        await gate.WaitAsync();try {
            if(await Healthy(m))return;
            var exe=Path.Combine(m.Root,"publish",m.Product+".exe");if(!File.Exists(exe))throw new Exception(m.Product+" 安装文件不存在。");
            using var process=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=m.Root,UseShellExecute=false,CreateNoWindow=true});
            for(int i=0;i<50;i++){await Task.Delay(200);if(await Healthy(m))return;if(process?.HasExited==true)throw new Exception(m.Product+" 启动失败，请检查原工具日志。");}
            throw new Exception(m.Product+" 启动超时。");
        }finally{gate.Release();}
    }
}
