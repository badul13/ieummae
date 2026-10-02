using System.Runtime.InteropServices;
using System.Text;

namespace Ieummae.App.Platform;

// 사용자 데이터 폴더 - %APPDATA%\ieummae
public static class AppData
{
    // IEUM_APPDATA - 미리보기 도구처럼 실제 설정을 건드리면 안 되는 실행용
    public static string Dir { get; } = Environment.GetEnvironmentVariable("IEUM_APPDATA") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ieummae");

    public static string PathOf(string name)
    {
        Directory.CreateDirectory(Dir);
        return Path.Combine(Dir, name);
    }
}

// 최근 커밋 메시지 - 최신순 20개, 메시지 사이 구분은 \x1e 한 글자
public static class MessageHistory
{
    const int Max = 20;
    const char Sep = '\x1e';
    static string File => AppData.PathOf("messages.txt");

    public static List<string> Load()
    {
        try { return System.IO.File.Exists(File) ? System.IO.File.ReadAllText(File).Split(Sep, StringSplitOptions.RemoveEmptyEntries).ToList() : []; }
        catch (IOException) { return []; }
    }

    public static void Add(string message)
    {
        message = message.Trim();
        if (message.Length == 0) return;
        var list = Load();
        list.Remove(message);
        list.Insert(0, message);
        try { System.IO.File.WriteAllText(File, string.Join(Sep, list.Take(Max)), new UTF8Encoding(false)); }
        catch (IOException) { }
    }
}

// 휴지통으로 보내기 - 추적 안 하는 파일 삭제는 되돌릴 수 있게
public static partial class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int SHFileOperation(ref SHFILEOPSTRUCT op);

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400;

    // 성공하면 true
    public static bool Send(IEnumerable<string> fullPaths)
    {
        // 경로마다 \0, 끝에 \0 하나 더
        var list = string.Concat(fullPaths.Select(p => p + "\0")) + "\0";
        var ptr = Marshal.StringToHGlobalUni(list);
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = ptr,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };
            return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
