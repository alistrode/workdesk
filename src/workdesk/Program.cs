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
ModelSetup.Bind(app, "http://127.0.0.1:11434", Environment.GetEnvironmentVariable("SETUP_MODEL") ?? "qwen3-vl:4b");
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

// ==================== 模型一键配置（自动注入） ====================
static class ModelSetup
{
    public sealed class Status
    {
        public string phase { get; set; } = "check";
        public string status { get; set; } = "";
        public long completed { get; set; }
        public long total { get; set; }
        public string? error { get; set; }
        public bool running { get; set; }
        public bool ready { get; set; }
        public bool ollamaRunning { get; set; }
        public bool ollamaInstalled { get; set; }
        public string model { get; set; } = "";
        public string[] installed { get; set; } = Array.Empty<string>();
    }

    sealed class Runner
    {
        public readonly object Gate = new();
        public Status Current = new();
        public string Endpoint = "";
        public string Model = "";
        readonly System.Net.Http.HttpClient http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        Uri U(string p) => new(Endpoint.TrimEnd('/') + p);

        public string Snap() { lock (Gate) return System.Text.Json.JsonSerializer.Serialize(Current); }
        public Status Get() { lock (Gate) return Current; }

        public async Task<bool> Ping()
        {
            try { using var r = await http.GetAsync(U("/api/version")); return r.IsSuccessStatusCode; } catch { return false; }
        }

        public async Task<string[]> Tags()
        {
            try
            {
                using var r = await http.GetAsync(U("/api/tags"));
                if (!r.IsSuccessStatusCode) return Array.Empty<string>();
                using var d = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                return d.RootElement.GetProperty("models").EnumerateArray()
                    .Select(x => x.GetProperty("name").GetString() ?? "")
                    .Where(s => s.Length > 0).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        static string BaseName(string n) { var i = n.IndexOf(':'); return i < 0 ? n : n.Substring(0, i); }

        public static string? FindOllama()
        {
            var cands = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
                @"E:\ruanj\Ollama\ollama.exe"
            };
            foreach (var c in cands) { try { if (File.Exists(c)) return c; } catch { } }
            return null;
        }

        public async Task<Status> Snapshot()
        {
            var up = await Ping();
            var tags = up ? await Tags() : Array.Empty<string>();
            var installed = FindOllama() is not null;
            lock (Gate)
            {
                Current.ollamaRunning = up;
                Current.ollamaInstalled = installed;
                Current.installed = tags;
                Current.model = Model;
                if (!Current.running)
                {
                    Current.ready = tags.Any(t => BaseName(t) == BaseName(Model));
                    Current.phase = Current.ready ? "done" : "check";
                    if (Current.ready) { Current.status = "模型已就绪"; Current.error = null; }
                }
                return Current;
            }
        }

        public void Start()
        {
            lock (Gate)
            {
                if (Current.running) return;
                Current = new Status { running = true, phase = "check", status = "检测环境", model = Model };
            }
            _ = Task.Run(RunAsync);
        }

        void Set(string phase, string status, long total = 0)
        {
            lock (Gate)
            {
                Current.phase = phase;
                Current.status = status;
                if (total > 0) { Current.total = total; Current.completed = 0; }
                else if (total == 0) { Current.total = 0; Current.completed = 0; }
            }
        }

        // ---------- Ollama 自动安装 ----------
        async Task<string> DownloadOllamaInstaller()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            try { if (File.Exists(tmp) && new FileInfo(tmp).Length > 100_000_000) return tmp; } catch { }

            Set("install-ollama", "下载 Ollama 安装包");
            using var r = await http.GetAsync("https://ollama.com/download/OllamaSetup.exe", System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (!r.IsSuccessStatusCode) throw new Exception("下载 Ollama 安装包失败：HTTP " + (int)r.StatusCode);

            Set("install-ollama", "下载 Ollama 安装包", r.Content.Headers.ContentLength ?? 0);
            using (var fs = File.Create(tmp))
            using (var s = await r.Content.ReadAsStreamAsync())
            {
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await s.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await fs.WriteAsync(buf, 0, n);
                    done += n;
                    lock (Gate) { Current.completed = done; }
                }
            }
            return tmp;
        }

        async Task EnsureOllamaInstalled()
        {
            if (FindOllama() is not null) return;
            var exe = await DownloadOllamaInstaller();

            Set("install-ollama", "静默安装 Ollama");
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true };
                psi.Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
                var proc = System.Diagnostics.Process.Start(psi);
                if (proc is not null)
                    for (var i = 0; i < 300 && !proc.HasExited; i++) await Task.Delay(1000);
            }
            catch (Exception e) { throw new Exception("无法运行 Ollama 安装程序：" + e.Message); }

            for (var i = 0; i < 40 && FindOllama() is null; i++) await Task.Delay(500);
            if (FindOllama() is null) throw new Exception("Ollama 安装后仍未找到 ollama.exe");
        }

        // ---------- 通道一：Ollama 官方 /api/pull ----------
        async Task PullViaApi()
        {
            Set("pull", "下载模型 " + Model);
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, U("/api/pull"))
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { model = Model, stream = true })
            };
            using var res = await http.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) throw new Exception("HTTP " + (int)res.StatusCode);

            using var stream = await res.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var d = System.Text.Json.JsonDocument.Parse(line);
                    var root = d.RootElement;
                    if (root.TryGetProperty("error", out var e)) throw new Exception(e.GetString() ?? "未知错误");
                    lock (Gate)
                    {
                        if (root.TryGetProperty("status", out var st)) Current.status = st.GetString() ?? Current.status;
                        if (root.TryGetProperty("total", out var t) && t.TryGetInt64(out var tv) && tv > 0) Current.total = tv;
                        if (root.TryGetProperty("completed", out var c) && c.TryGetInt64(out var cv)) Current.completed = cv;
                        Current.phase = "pull";
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }
        }

        // ---------- 通道二：自己下载 GGUF + 本地导入（绕过代理/fake-ip 限制） ----------
        async Task ImportFromRegistry()
        {
            var name = Model.Contains(':') ? Model.Split(':')[0] : Model;
            var tag = Model.Contains(':') ? Model.Split(':')[1] : "latest";
            var reg = "https://registry.ollama.ai/v2/library/" + name;

            Set("pull", "获取模型清单 " + Model);
            using var mreq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, reg + "/manifests/" + tag);
            mreq.Headers.TryAddWithoutValidation("Accept", "application/vnd.docker.distribution.manifest.v2+json");
            using var mres = await http.SendAsync(mreq);
            if (!mres.IsSuccessStatusCode) throw new Exception("获取模型清单失败：HTTP " + (int)mres.StatusCode);
            using var md = System.Text.Json.JsonDocument.Parse(await mres.Content.ReadAsStringAsync());

            string? digest = null;
            long size = 0;
            foreach (var l in md.RootElement.GetProperty("layers").EnumerateArray())
            {
                var mt = l.GetProperty("mediaType").GetString() ?? "";
                if (!mt.Contains("image.model")) continue;
                digest = l.GetProperty("digest").GetString();
                size = l.GetProperty("size").GetInt64();
                break;
            }
            if (digest is null) throw new Exception("模型清单中没有可用的模型层");

            var dir = Path.Combine(Path.GetTempPath(), "model-setup");
            Directory.CreateDirectory(dir);
            var gguf = Path.Combine(dir, name + "-" + tag + ".gguf");

            Set("pull", "下载模型 " + Model, size);
            using (var rres = await http.GetAsync(reg + "/blobs/" + digest, System.Net.Http.HttpCompletionOption.ResponseHeadersRead))
            {
                if (!rres.IsSuccessStatusCode) throw new Exception("下载模型失败：HTTP " + (int)rres.StatusCode);
                using var fs = File.Create(gguf);
                using var s = await rres.Content.ReadAsStreamAsync();
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await s.ReadAsync(buf, 0, buf.Length)) > 0)
                {
                    await fs.WriteAsync(buf, 0, n);
                    done += n;
                    lock (Gate) { Current.completed = done; }
                }
            }

            Set("verify", "导入模型 " + Model);
            var mf = Path.Combine(dir, name + "-" + tag + ".Modelfile");
            await File.WriteAllTextAsync(mf, "FROM " + gguf.Replace('\\', '/') + "\n");

            var exe = FindOllama() ?? throw new Exception("找不到 ollama.exe，无法导入模型");
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("create");
            psi.ArgumentList.Add(Model);
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(mf);
            psi.Environment["OLLAMA_HOST"] = Endpoint.Replace("http://", "").Replace("https://", "");

            using var proc = System.Diagnostics.Process.Start(psi) ?? throw new Exception("无法启动 ollama create");
            var so = proc.StandardOutput.ReadToEndAsync();
            var se = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var text = ((await so) + (await se)).Trim();
            if (proc.ExitCode != 0) throw new Exception("导入模型失败：" + text);

            try { File.Delete(gguf); } catch { }
            try { File.Delete(mf); } catch { }
        }

        async Task RunAsync()
        {
            try
            {
                Set("check", "检测环境");
                if (FindOllama() is null) await EnsureOllamaInstalled();

                if (!await Ping())
                {
                    Set("start-ollama", "启动 Ollama");
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(FindOllama()!) { UseShellExecute = false, CreateNoWindow = true };
                        psi.ArgumentList.Add("serve");
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch { }

                    var up = false;
                    for (var i = 0; i < 120; i++) { await Task.Delay(500); if (await Ping()) { up = true; break; } }
                    if (!up) throw new Exception("Ollama 启动超时（60 秒内未响应 /api/version）");
                }

                Set("pull", "检查模型");
                var tags = await Tags();
                if (tags.Any(t => BaseName(t) == BaseName(Model)))
                {
                    lock (Gate) { Current.ready = true; Current.running = false; Current.phase = "done"; Current.status = "模型已就绪"; }
                    return;
                }

                string? firstError = null;
                try { await PullViaApi(); }
                catch (Exception ex) { firstError = ex.Message; }

                var afterPull = await Tags();
                if (!afterPull.Any(t => BaseName(t) == BaseName(Model)))
                {
                    // 官方通道失败时自动改走本地导入通道（常见于代理 / fake-ip DNS 环境）
                    try { await ImportFromRegistry(); }
                    catch (Exception ex2)
                    {
                        var msg = firstError is null ? ex2.Message : ("官方通道：" + firstError + "；本地导入通道：" + ex2.Message);
                        throw new Exception(msg);
                    }
                }

                Set("verify", "校验模型");
                var after = await Tags();
                if (!after.Any(t => BaseName(t) == BaseName(Model))) throw new Exception("下载结束但模型未出现在 /api/tags 中");
                lock (Gate) { Current.installed = after; Current.ready = true; Current.running = false; Current.phase = "done"; Current.status = "模型已就绪"; }
            }
            catch (Exception ex)
            {
                lock (Gate) { Current.running = false; Current.phase = "error"; Current.error = ex.Message; }
            }
        }
    }

    public static void Bind(WebApplication app, string endpoint, string model)
    {
        var runner = new Runner { Endpoint = endpoint, Model = model };

        app.MapGet("/api/setup/state", async () => await runner.Snapshot());

        app.MapPost("/api/setup/ensure-model", async () =>
        {
            await runner.Snapshot();
            runner.Start();
            return Results.Accepted("/api/setup/state", new { ok = true, model });
        });

        app.MapGet("/api/setup/progress", async (HttpContext ctx) =>
        {
            ctx.Response.Headers["Content-Type"] = "text/event-stream; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-cache";
            ctx.Response.Headers["X-Accel-Buffering"] = "no";
            for (var i = 0; i < 6000; i++)
            {
                await ctx.Response.WriteAsync("data: " + runner.Snap() + "\n\n");
                await ctx.Response.Body.FlushAsync();
                var s = runner.Get();
                if (!s.running && (s.phase == "done" || s.phase == "error")) break;
                try { await Task.Delay(300, ctx.RequestAborted); } catch { break; }
            }
        });
    }
}
