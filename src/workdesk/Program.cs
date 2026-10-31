using System.Diagnostics;
using System.Text.Json;

var modules = new Modules();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.UseUrls($"http://127.0.0.1:{modules.Port}");
builder.Services.AddSingleton(modules);
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(10) });
var app = builder.Build();
app.Use(async (ctx, next) => {
    if (ctx.Request.Host.Host is not ("127.0.0.1" or "localhost")) { ctx.Response.StatusCode = 403; return; }
    if (ctx.Request.Method != "GET" && ctx.Request.Headers.Origin.Count > 0 && ctx.Request.Headers.Origin.ToString() != $"http://{ctx.Request.Host}") { ctx.Response.StatusCode = 403; return; }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/health", () => new { product = "WorkDesk", ok = true, port = modules.Port });
app.MapGet("/api/modules", async (Modules m) => await m.Status());
app.MapPost("/api/modules/{key}/start", async (string key, Modules m) => {
    try { await m.Ensure(key); return Results.Ok(new { ok = true }); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapMethods("/m/{key}/{**path}", ["GET", "POST", "DELETE", "PUT"], async (string key, string? path, HttpContext ctx, Modules m, HttpClient http) => {
    if (!m.Items.TryGetValue(key, out var module)) { ctx.Response.StatusCode = 404; return; }
    try {
        await m.Ensure(key);
        using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), module.Url + (path ?? "") + ctx.Request.QueryString);
        if (ctx.Request.Method is not ("GET" or "HEAD")) { request.Content = new StreamContent(ctx.Request.Body); if (ctx.Request.ContentType != null) request.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType); request.Headers.TryAddWithoutValidation("Origin", module.Url.TrimEnd('/')); }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
        ctx.Response.StatusCode = (int)response.StatusCode;
        ctx.Response.ContentType = response.Content.Headers.ContentType?.ToString();
        if (response.Headers.Location is { } location) ctx.Response.Headers.Location = $"/m/{key}/" + location.ToString().TrimStart('/');
        if (ctx.Response.ContentType?.Contains("text/html") == true) {
            var html = await response.Content.ReadAsStringAsync(ctx.RequestAborted);
            html = html.Replace("href=\"/", $"href=\"/m/{key}/").Replace("src=\"/", $"src=\"/m/{key}/");
            var hook = "<script>window.workdeskModule=" + JsonSerializer.Serialize(key) + ";const originalFetch=window.fetch.bind(window);window.fetch=(url,opts)=>originalFetch(typeof url==='string'&&url.startsWith('/api/')?'/m/'+window.workdeskModule+url:url,opts);</script>";
            html = html.Replace("<head>", "<head><base href=\"/m/" + key + "/\">" + hook).Replace("</body>", "<script src=\"/bridge.js\" defer></script></body>");
            await ctx.Response.WriteAsync(html);
        } else await response.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    } catch (Exception ex) { if (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 502; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); } }
});
var packetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkDesk", "materials");
Directory.CreateDirectory(packetRoot);
app.MapGet("/api/materials", () => Directory.EnumerateFiles(packetRoot, "*.json").Select(p => JsonSerializer.Deserialize<Material>(File.ReadAllText(p))).Where(p => p != null).OrderByDescending(p => p!.CreatedAt));
app.MapPost("/api/materials", (MaterialInput input) => {
    if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Text) || input.Title.Length > 200 || input.Text.Length > 200000) return Results.BadRequest(new { error = "请填写标题和内容；内容最多 20 万字。" });
    var material = new Material(Guid.NewGuid().ToString("N"), input.Title, input.Text, input.Source ?? "手动", DateTimeOffset.Now);
    var path = Path.Combine(packetRoot, material.Id + ".json"); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(material)); File.Move(path + ".tmp", path, true);
    return Results.Ok(material);
});
app.MapDelete("/api/materials/{id}", (string id) => { if (!Guid.TryParseExact(id, "N", out _)) return Results.BadRequest(); var p = Path.Combine(packetRoot, id + ".json"); if (!File.Exists(p)) return Results.NotFound(); File.Delete(p); return Results.NoContent(); });
app.MapPost("/api/guio/open", (Modules m) => {
    if (m.GuioApp is null) return Results.BadRequest(new { error = m.GuioHint });
    var p = Process.Start(new ProcessStartInfo(m.GuioApp) { WorkingDirectory = m.GuioRoot, UseShellExecute = true }); return Results.Ok(new { ok = p != null });
});
app.MapPost("/api/guio/{command}", async (string command, Modules m) => {
    if (command is not ("ping" or "devices")) return Results.BadRequest();
    if (m.GuioCli is null) return Results.BadRequest(new { error = m.GuioHint });
    try {
        using var p = new Process { StartInfo = new ProcessStartInfo(m.GuioCli) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = m.GuioRoot } };
        p.StartInfo.ArgumentList.Add(command); p.Start(); var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)); try { await p.WaitForExitAsync(timeout.Token); } catch { if (!p.HasExited) p.Kill(true); throw new Exception("设备检查超时，请打开 GuIO 检查连接。"); }
        return Results.Ok(new { ok = p.ExitCode == 0, output = await output, error = await error });
    } catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.Run();

record MaterialInput(string Title, string Text, string? Source);
record Material(string Id, string Title, string Text, string Source, DateTimeOffset CreatedAt);

sealed record Module(string Key, string Product, int Port, string? Root)
{
    public string Url => $"http://127.0.0.1:{Port}/";
    public string? Exe => Root is null ? null : Path.Combine(Root, "publish", Product + ".exe");
    public bool ExeFound => Exe is not null && File.Exists(Exe);
}

static class Locate
{
    public static string BaseDir => AppContext.BaseDirectory;

    public static string? FromFile(string key)
    {
        try
        {
            var p = Path.Combine(BaseDir, "workdesk.config.json");
            if (!File.Exists(p)) return null;
            using var d = JsonDocument.Parse(File.ReadAllText(p));
            if (!d.RootElement.TryGetProperty("paths", out var paths)) return null;
            if (!paths.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) return null;
            var s = v.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch { return null; }
    }

    static string? Expand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
            var full = Path.IsPathRooted(expanded) ? expanded : Path.Combine(BaseDir, expanded);
            return Path.GetFullPath(full);
        }
        catch { return null; }
    }

    /// <summary>只有当目录里真的存在 probe 指向的文件时才算命中安装目录。</summary>
    public static string? ResolveInstall(string key, string? envName, string probe, params string?[] candidates)
    {
        var all = new List<string?>();
        if (!string.IsNullOrWhiteSpace(envName)) all.Add(Environment.GetEnvironmentVariable(envName));
        all.Add(FromFile(key));
        foreach (var c in candidates) all.Add(c);
        foreach (var raw in all)
        {
            var full = Expand(raw);
            if (full is not null && Directory.Exists(full) && File.Exists(Path.Combine(full, probe))) return full;
        }
        return null;
    }

    /// <summary>仅用于给出更准确的提示：找到一个存在但内容不全的目录。</summary>
    public static string? FirstExisting(string key, string? envName, params string?[] candidates)
    {
        var all = new List<string?>();
        if (!string.IsNullOrWhiteSpace(envName)) all.Add(Environment.GetEnvironmentVariable(envName));
        all.Add(FromFile(key));
        foreach (var c in candidates) all.Add(c);
        foreach (var raw in all)
        {
            var full = Expand(raw);
            if (full is not null && Directory.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>在同级目录里按名字模糊查找（例如 agenthub-1.1.0 / AgentHub / agents-hub 都能命中 AgentHub）。</summary>
    public static string? Scan(string product, string probeRelative)
    {
        try
        {
            // 去掉尾部分隔符，否则 GetDirectoryName 会返回目录自身，导致少上一层
            var baseFull = Path.GetFullPath(BaseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (var up = 1; up <= 4; up++)
            {
                var parent = baseFull;
                for (var i = 0; i < up; i++) parent = Path.GetDirectoryName(parent) ?? parent;
                if (!Directory.Exists(parent)) continue;
                foreach (var d in Directory.GetDirectories(parent))
                {
                    var name = Path.GetFileName(d);
                    if (!(string.Equals(name, product, StringComparison.OrdinalIgnoreCase) ||
                          name.Contains(product, StringComparison.OrdinalIgnoreCase))) continue;
                    if (File.Exists(Path.Combine(d, probeRelative))) return d;
                    // 用户解压后常见多套一层版本目录（如 researchdesk\researchdesk-1.1.0\publish\...）
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(d))
                            if (File.Exists(Path.Combine(sub, probeRelative))) return sub;
                    }
                    catch { }
                }
            }
        }
        catch { }
        return null;
    }
}

sealed class Modules
{
    public int Port { get; }
    public string? GuioRoot { get; }
    public string? GuioApp => GuioRoot is null ? null : Path.Combine(GuioRoot, "guio-app.exe");
    public string? GuioCli => GuioRoot is null ? null : Path.Combine(GuioRoot, "cli", "guio.exe");
    public string GuioHint { get; }

    public Dictionary<string, Module> Items { get; } = new();

    public Modules()
    {
        var envPort = Environment.GetEnvironmentVariable("WORKDESK_PORT");
        Port = int.TryParse(envPort, out var p) && p is > 0 and <= 65535 ? p : 5190;

        Items["research"] = Build("research", "ResearchDesk", 5182, "WORKDESK_RESEARCHDESK_ROOT");
        Items["prompt"] = Build("prompt", "PromptLab", 5181, "WORKDESK_PROMPTLAB_ROOT");
        Items["agent"] = Build("agent", "AgentHub", 5178, "WORKDESK_AGENTHUB_ROOT");

        string?[] guioSibling = { @"..\GuIO", @"..\..\GuIO" };
        string?[] guioLegacy = { @"E:\GuIO", @"C:\Users\Acer\GuIO" };

        GuioRoot = Locate.ResolveInstall("guio", "WORKDESK_GUIO_ROOT", "guio-app.exe", guioSibling);
        GuioRoot ??= Locate.Scan("GuIO", "guio-app.exe");
        GuioRoot ??= Locate.ResolveInstall("guio", null, "guio-app.exe", guioLegacy);
        GuioRoot ??= Locate.FirstExisting("guio", "WORKDESK_GUIO_ROOT", guioSibling);
        GuioHint = GuioRoot is null
            ? "未找到 GuIO。请把 GuIO 放到 WorkDesk 同级目录，或设置环境变量 WORKDESK_GUIO_ROOT，或在 workdesk.config.json 的 paths.guio 指定绝对路径。"
            : (File.Exists(GuioApp) ? "已找到 GuIO" : $"找到了 GuIO 目录 {GuioRoot}，但缺少 guio-app.exe");
    }

    static Module Build(string key, string product, int port, string envName)
    {
        var probe = Path.Combine("publish", product + ".exe");
        string?[] sibling =
        {
            Path.Combine("..", product),
            Path.Combine("..", "..", product),
            Path.Combine("..", "apps", product),
            product
        };
        string?[] legacy = { $@"E:\xiangmujihua\apps\{product}" };

        // 优先级：环境变量 / 配置文件 → 同级目录按名匹配 → 同级目录模糊扫描 → 旧固定路径兜底
        var root = Locate.ResolveInstall(key, envName, probe, sibling);
        root ??= Locate.Scan(product, probe);
        root ??= Locate.ResolveInstall(key, null, probe, legacy);
        root ??= Locate.FirstExisting(key, envName, sibling);
        return new Module(key, product, port, root);
    }

    readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
    readonly SemaphoreSlim gate = new(1, 1);

    async Task<bool> Healthy(Module m)
    {
        try { var data = await client.GetFromJsonAsync<JsonElement>(m.Url + "api/health"); return data.GetProperty("product").GetString() == m.Product; }
        catch { return false; }
    }

    public async Task<object[]> Status() => await Task.WhenAll(Items.Select(async kv => (object)new
    {
        key = kv.Key,
        name = kv.Value.Product,
        port = kv.Value.Port,
        root = kv.Value.Root,
        exeFound = kv.Value.ExeFound,
        ready = await Healthy(kv.Value),
        hint = kv.Value.Root is null
            ? $"未找到 {kv.Value.Product}。放到 WorkDesk 同级目录，或设置环境变量 WORKDESK_{kv.Value.Product.ToUpperInvariant()}_ROOT。"
            : (kv.Value.ExeFound ? null : $"{kv.Value.Root} 下缺少 publish\\{kv.Value.Product}.exe")
    }));

    public async Task Ensure(string key)
    {
        if (!Items.TryGetValue(key, out var m)) throw new Exception("未知模块。");
        await gate.WaitAsync();
        try
        {
            if (await Healthy(m)) return;
            if (m.Root is null) throw new Exception($"未找到 {m.Product} 的安装目录。请把 {m.Product} 解压到 WorkDesk 同级目录，或设置环境变量 WORKDESK_{m.Product.ToUpperInvariant()}_ROOT 指向它。");
            var exe = m.Exe!;
            if (!File.Exists(exe)) throw new Exception($"{m.Product} 安装文件不存在：{exe}");
            using var process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = m.Root, UseShellExecute = false, CreateNoWindow = true });
            for (int i = 0; i < 50; i++) { await Task.Delay(200); if (await Healthy(m)) return; if (process?.HasExited == true) throw new Exception(m.Product + " 启动失败，请检查原工具日志。"); }
            throw new Exception(m.Product + " 启动超时。");
        }
        finally { gate.Release(); }
    }
}
