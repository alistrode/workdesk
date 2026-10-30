using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
const string url="http://127.0.0.1:5190/";
using var client=new HttpClient {Timeout=TimeSpan.FromSeconds(2)};
bool Ready(){try{return client.GetFromJsonAsync<JsonElement>(url+"api/health").GetAwaiter().GetResult().GetProperty("product").GetString()=="WorkDesk";}catch{return false;}}
try {
    using var gate=new Mutex(false,"Local\\WorkDesk.Start"); if(!gate.WaitOne(TimeSpan.FromSeconds(45)))throw new Exception("另一个启动器仍在启动工作台，请稍后重试。");
    try {
        if(!Ready()) {
            var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","publish"));
            using var p=Process.Start(new ProcessStartInfo(Path.Combine(root,"WorkDesk.exe")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true});
            for(var i=0;i<60&&!Ready();i++){Thread.Sleep(250);if(p?.HasExited==true)throw new Exception("WorkDesk 服务未能启动。");}
            if(!Ready())throw new Exception("WorkDesk 启动超时，请检查 5190 端口。");
        }
    }finally{gate.ReleaseMutex();}
    var browsers=new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe")};
    var browser=browsers.FirstOrDefault(File.Exists);
    if(browser!=null){var info=new ProcessStartInfo(browser){UseShellExecute=true};info.ArgumentList.Add("--app="+url);Process.Start(info);}
    else Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
}catch(Exception ex){var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WorkDesk","logs");Directory.CreateDirectory(root);var p=Path.Combine(root,"launcher-error.txt");File.WriteAllText(p,ex.Message);Process.Start(new ProcessStartInfo(p){UseShellExecute=true});}
