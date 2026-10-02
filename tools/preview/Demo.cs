using Ieummae.Core.Git;

// 미리보기용 데모 저장소 - 임시 폴더에 매번 새로 만듦 (작업 트리 변경·브랜치·태그·병합)
// 실제 저장소는 읽기만 하고, 쓰는 동작이 필요한 화면은 여기서 확인
static class Demo
{
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "ieummae-preview-demo");

    public static string Create()
    {
        Reset(Root);
        Directory.CreateDirectory(Root);
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "양털");
        Git("config", "user.email", "wool@example.com");
        Git("config", "commit.gpgsign", "false");

        Write("README.md", "# 목장 일지\n\n양들의 하루를 기록합니다.\n");
        Write("src/Flock.cs", Flock("count", "Length"));
        Commit("프로젝트 뼈대", "2026-09-20T10:00:00");
        Write("docs/설계.md", "## 구조\n\n- 목장\n- 양\n- 단추\n");
        Commit("설계 문서 추가", "2026-09-21T11:30:00");
        Git("tag", "v0.1.0");

        Git("checkout", "-q", "-b", "feat/털깎기");
        Write("src/Shear.cs", "namespace Farm;\n\npublic static class Shear\n{\n    public static int Wool(int sheep) => sheep * 3;\n}\n");
        Commit("털깎기 계산 추가", "2026-09-22T09:10:00");
        Write("src/Shear.cs", "namespace Farm;\n\npublic static class Shear\n{\n    // 양 한 마리당 털 3kg\n    public static int Wool(int sheep) => sheep * 3;\n}\n");
        Commit("털깎기 주석", "2026-09-23T14:00:00");
        Git("checkout", "-q", "main");
        Write("README.md", "# 목장 일지\n\n양들의 하루를 기록합니다.\n\n## 실행\n\n`dotnet run`\n");
        Commit("README 실행 방법", "2026-09-24T16:20:00");
        Env("2026-09-25T10:00:00", () => Git("merge", "-q", "--no-ff", "feat/털깎기", "-m", "Merge branch 'feat/털깎기'"));
        Git("checkout", "-q", "-b", "feat/울타리");
        Write("src/Fence.cs", "namespace Farm;\n\npublic record Fence(int Length);\n");
        Commit("울타리 모델", "2026-09-27T13:45:00");
        Git("checkout", "-q", "main");
        // 원격 - 맨 저장소에 여기까지 올려 두고, 다음 커밋은 올리지 않음 (↑1 표시)
        var origin = Root + "-origin";
        Reset(origin);
        Ieummae.Core.Git.Git.RunAsync(Path.GetTempPath(), "init", "-q", "--bare", "-b", "main", origin).GetAwaiter().GetResult();
        Git("remote", "add", "origin", origin);
        Git("push", "-q", "-u", "origin", "main", "feat/털깎기", "feat/울타리");
        Write("docs/설계.md", "## 구조\n\n- 목장\n- 양\n- 단추\n- 울타리\n");
        Commit("설계에 울타리 추가", "2026-09-28T08:30:00");

        // 작업 트리 변경 - 단어 단위 수정, 줄 추가·삭제, 새 파일, 삭제
        Write("src/Flock.cs", Flock("total", "Count").Replace("    // 무리 정리\n    public void Tidy() { }\n", "") + "\n// 다음: 울타리 연결\n");
        Write("메모.txt", "털실 색 고르기\n단추 여섯 개\n");
        File.Delete(Path.Combine(Root, "docs", "설계.md"));
        return Root;
    }

    static string Flock(string name, string prop) => $$"""
        using System.Collections.Generic;

        namespace Farm;

        // 양 무리 - 이름과 털 무게
        public sealed class Flock
        {
            readonly List<string> _sheep = new();

            public int Size => _sheep.{{prop}};

            public void Add(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                _sheep.Add(name);
            }

            public string Describe()
            {
                var {{name}} = _sheep.{{prop}};
                return $"양 {{{name}}}마리";
            }

            // 무리 정리
            public void Tidy() { }
        }

        """.Replace("\r", "");

    // 충돌 중인 저장소 - 같은 줄을 양쪽에서 고친 파일(덩어리 둘) + 상대가 지운 파일
    public static string CreateConflict()
    {
        var root = Root + "-conflict";
        Reset(root);
        Directory.CreateDirectory(root);
        void G(params string[] a)
        {
            var r = Ieummae.Core.Git.Git.RunAsync(root, a).GetAwaiter().GetResult();
            if (!r.Ok && a[0] != "merge") throw new InvalidOperationException(r.Error);
        }
        void W(string p, string t) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, p))!); File.WriteAllText(Path.Combine(root, p), t); }
        G("init", "-q", "-b", "main");
        G("config", "user.name", "양털"); G("config", "user.email", "wool@example.com"); G("config", "commit.gpgsign", "false");
        G("config", "merge.conflictStyle", "merge");
        var baseText = "namespace Farm;\n\npublic static class Wool\n{\n    public const string Color = \"cream\";\n\n    public static int Buttons(int sheep) => sheep * 2;\n\n    public static string Name => \"양털\";\n}\n";
        W("src/Wool.cs", baseText);
        W("docs/옛 설명.md", "# 옛 설명\n");
        G("add", "-A"); G("commit", "-q", "-m", "기본");
        G("checkout", "-q", "-b", "feat/색");
        W("src/Wool.cs", baseText.Replace("\"cream\"", "\"oatmeal\"").Replace("sheep * 2", "sheep * 4 // 앞뒤 두 개씩"));
        File.Delete(Path.Combine(root, "docs", "옛 설명.md"));
        G("add", "-A"); G("commit", "-q", "-m", "털실 색과 단추 수");
        G("checkout", "-q", "main");
        W("src/Wool.cs", baseText.Replace("\"cream\"", "\"snow\"").Replace("sheep * 2", "sheep * 3"));
        W("docs/옛 설명.md", "# 옛 설명\n\n고친 내용\n");
        G("add", "-A"); G("commit", "-q", "-m", "흰 털, 단추 셋");
        G("merge", "feat/색");
        return root;
    }

    // 지난 실행 흔적 지우기 - .git 객체는 읽기 전용이라 속성부터 풂
    static void Reset(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }

    static void Write(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    static void Commit(string msg, string date) => Env(date, () => { Git("add", "-A"); Git("commit", "-q", "-m", msg); });

    // 커밋 날짜 고정 - 미리보기 그림이 실행할 때마다 같게
    static void Env(string date, Action a)
    {
        Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", date);
        Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", date);
        a();
        Environment.SetEnvironmentVariable("GIT_AUTHOR_DATE", null);
        Environment.SetEnvironmentVariable("GIT_COMMITTER_DATE", null);
    }

    static void Git(params string[] args)
    {
        var r = Ieummae.Core.Git.Git.RunAsync(Root, args).GetAwaiter().GetResult();
        if (!r.Ok) throw new InvalidOperationException($"git {string.Join(' ', args)}: {r.Error}");
    }
}
