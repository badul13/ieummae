using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ieummae.App;
using Ieummae.App.Theme;
using Ieummae.App.Views;
using Ieummae.App.Windows;
using Ieummae.Core;

// 화면 미리보기 - 실제 창을 띄우지 않고 헤드리스로 그려 PNG 저장, 브라우저용 index.html 갱신
// 사용: dotnet run --project tools/preview -- <저장소 경로> [출력 폴더] [이름 필터]
// 출력 폴더 기본값 tools/preview/out (gitignore). 페이지는 2초마다 새 그림 확인
var repo = args.Length > 0 ? Path.GetFullPath(args[0]) : Environment.CurrentDirectory;
var outDir = Path.GetFullPath(args.Length > 1 && args[1] != "-" ? args[1] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "out"));
var filter = args.Length > 2 ? args[2] : null;
Directory.CreateDirectory(outDir);
var demo = Demo.Create();

// 렌더링은 Skia 그대로 (실제 앱의 소프트웨어 렌더링과 같은 그림), 창 시스템만 헤드리스
AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .With(App.Fonts)
    .SetupWithoutStarting();

// 화면 목록 - (파일 이름, 설명, 테마, 창 만들기, 띄운 뒤 조작)
var cases = new List<(string Name, string Label, bool Dark, Func<Window> Create, Action<Window>? Setup)>
{
    ("log-light", "Log · 회사 저장소", false, () => Open("log", repo), null),
    ("log-dark", "Log · 회사 저장소", true, () => Open("log", repo), null),
    ("commit-light", "Commit · 데모 저장소", false, () => Open("commit", demo), null),
    ("commit-dark", "Commit · 데모 저장소", true, () => Open("commit", demo), null),
    ("diff-unified", "Diff 통합 · 데모 작업 트리", false, () => Open("diff", Path.Combine(demo, "src", "Flock.cs")), w => Diff(w).IsSplit = false),
    ("diff-split", "Diff 좌우 · 데모 작업 트리", true, () => Open("diff", Path.Combine(demo, "src", "Flock.cs")), w => Diff(w).IsSplit = true),
    ("diff-untracked", "Diff 새 파일 · 데모", false, () => Open("diff", Path.Combine(demo, "메모.txt")), w => Diff(w).IsSplit = false),
    ("message", "저장소 밖 안내", true, () => Open("log", Path.GetTempPath()), null),
};

var shots = new List<(string File, string Label)>();
foreach (var c in cases)
{
    var file = c.Name + ".png";
    shots.Add((file, c.Label));
    if (filter is not null && !c.Name.Contains(filter)) continue;
    Tone.Apply(Application.Current!, c.Dark);
    var w = c.Create();
    w.Show();
    Pump(40);
    if (c.Setup is { } setup) { setup(w); Pump(20); }
    w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, file), PngBitmapEncoderOptions.Default);
    w.Close();
}

// 갱신 표시 - 페이지가 이 값이 바뀌면 그림을 다시 읽음 (file:// 라 fetch 대신 script 태그)
var stamp = DateTime.Now.ToString("HH:mm:ss");
File.WriteAllText(Path.Combine(outDir, "stamp.js"), $"window.__stamp = '{stamp}';");
File.WriteAllText(Path.Combine(outDir, "index.html"), Page(shots));
Console.WriteLine($"{shots.Count}장 {stamp} → {Path.Combine(outDir, "index.html")}");

static Window Open(string cmd, string path) => Launcher.Create(new CommandLine(cmd, path));

static DiffView Diff(Window w) => w.GetVisualDescendants().OfType<DiffView>().First();

// 창 표시 후 비동기 git 조회 결과까지 반영되게 잠시 돌림
static void Pump(int n)
{
    for (int i = 0; i < n; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(25);
    }
}

static string Page(List<(string File, string Label)> shots) => $$"""
<!doctype html>
<html lang="ko"><head><meta charset="utf-8"><title>ieummae 미리보기</title>
<style>
  body { margin: 0; padding: 24px; background: #2b2826; color: #ede6da; font: 14px system-ui, sans-serif; }
  header { display: flex; gap: 12px; align-items: baseline; margin-bottom: 16px; }
  h1 { font-size: 18px; margin: 0; } #stamp { color: #a59c90; }
  main { display: grid; grid-template-columns: repeat(auto-fill, minmax(620px, 1fr)); gap: 20px; }
  figure { margin: 0; } figcaption { margin-bottom: 6px; color: #a59c90; }
  img { width: 100%; border-radius: 8px; display: block; cursor: zoom-in; }
  img.big { position: fixed; inset: 0; width: auto; max-width: 100vw; max-height: 100vh; margin: auto; z-index: 9; cursor: zoom-out; box-shadow: 0 0 0 100vmax #000c; }
</style></head><body>
<header><h1>ieummae 미리보기</h1><span id="stamp"></span></header>
<main>
{{string.Join("\n", shots.Select(s => $"""  <figure><figcaption>{s.Label}</figcaption><img src="{s.File}" data-src="{s.File}"></figure>"""))}}
</main>
<script>
  // 그림 클릭 - 원본 크기로 보기
  document.querySelectorAll('img').forEach(i => i.onclick = () => i.classList.toggle('big'));
  // 2초마다 stamp.js 다시 읽어 바뀌었으면 그림 새로 고침
  let last = null;
  function poll() {
    const s = document.createElement('script');
    s.src = 'stamp.js?' + Date.now();
    s.onload = () => {
      s.remove();
      if (window.__stamp !== last) {
        last = window.__stamp;
        document.getElementById('stamp').textContent = '갱신 ' + last;
        document.querySelectorAll('img').forEach(i => i.src = i.dataset.src + '?' + Date.now());
      }
    };
    document.head.appendChild(s);
  }
  poll(); setInterval(poll, 2000);
</script>
</body></html>
""";
