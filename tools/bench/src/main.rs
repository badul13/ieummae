// 공통 측정 도구 - 창 표시 지연 + 프로세스 트리 메모리
// 사용: bench <라벨> <회수> <대기초> <exe> [인자...]
//   인자 안 {id} 는 회차별 id 로 치환, 창 제목 "ieummae-{id}" 를 기다림
//   제목 대신 pid 로 찾을 때: 라벨 앞에 "pid:" (TortoiseGit 등)
// 출력: 회차별 줄 + 마지막에 JSON 한 줄 (RESULT {...})
use std::process::Command;
use std::time::{Duration, Instant};
use windows_sys::Win32::Foundation::{CloseHandle, BOOL, HWND, LPARAM};
use windows_sys::Win32::System::Diagnostics::ToolHelp::*;
use windows_sys::Win32::System::ProcessStatus::{GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS, PROCESS_MEMORY_COUNTERS_EX};
use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_QUERY_LIMITED_INFORMATION, PROCESS_VM_READ};
use windows_sys::Win32::UI::WindowsAndMessaging::*;

struct Find { title: Option<String>, pids: Vec<u32>, found: HWND }

unsafe extern "system" fn cb(h: HWND, l: LPARAM) -> BOOL {
    let f = &mut *(l as *mut Find);
    if IsWindowVisible(h) == 0 || !GetWindow(h, GW_OWNER).is_null() { return 1; }
    let mut buf = [0u16; 256];
    let n = GetWindowTextW(h, buf.as_mut_ptr(), 256);
    if n == 0 { return 1; }
    let t = String::from_utf16_lossy(&buf[..n as usize]);
    let mut pid = 0;
    GetWindowThreadProcessId(h, &mut pid);
    let ok = match &f.title { Some(want) => &t == want, None => f.pids.contains(&pid) };
    if ok { f.found = h; return 0; }
    1
}

// 프로세스 스냅샷 - (pid, 부모 pid, 이름)
fn snapshot() -> Vec<(u32, u32, String)> {
    let mut v = Vec::new();
    unsafe {
        let s = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        let mut e: PROCESSENTRY32W = std::mem::zeroed();
        e.dwSize = std::mem::size_of::<PROCESSENTRY32W>() as u32;
        if Process32FirstW(s, &mut e) != 0 {
            loop {
                let n = e.szExeFile.iter().position(|&c| c == 0).unwrap_or(260);
                v.push((e.th32ProcessID, e.th32ParentProcessID, String::from_utf16_lossy(&e.szExeFile[..n])));
                if Process32NextW(s, &mut e) == 0 { break; }
            }
        }
        CloseHandle(s);
    }
    v
}

// 루트 + 자손 pid 전부
fn tree(root: u32) -> Vec<(u32, String)> {
    let all = snapshot();
    let mut out = vec![];
    let mut stack = vec![root];
    while let Some(p) = stack.pop() {
        if let Some((_, _, name)) = all.iter().find(|x| x.0 == p) { out.push((p, name.clone())); }
        for c in all.iter().filter(|x| x.1 == p && x.0 != p) { stack.push(c.0); }
    }
    out
}

// (작업 집합, 전용 커밋) 바이트
fn mem(pid: u32) -> (usize, usize) {
    unsafe {
        let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, 0, pid);
        if h.is_null() { return (0, 0); }
        let mut c: PROCESS_MEMORY_COUNTERS_EX = std::mem::zeroed();
        c.cb = std::mem::size_of::<PROCESS_MEMORY_COUNTERS_EX>() as u32;
        GetProcessMemoryInfo(h, &mut c as *mut _ as *mut PROCESS_MEMORY_COUNTERS, c.cb);
        CloseHandle(h);
        (c.WorkingSetSize, c.PrivateUsage)
    }
}

fn mb(b: usize) -> f64 { b as f64 / 1048576.0 }

fn median(v: &[f64]) -> f64 {
    let mut s = v.to_vec();
    s.sort_by(|a, b| a.partial_cmp(b).unwrap());
    s[s.len() / 2]
}

fn main() {
    let a: Vec<String> = std::env::args().collect();
    if a.len() < 5 { eprintln!("bench <label> <n> <waitsec> <exe> [args..]"); std::process::exit(2); }
    let (by_pid, label) = match a[1].strip_prefix("pid:") { Some(l) => (true, l.to_string()), None => (false, a[1].clone()) };
    let n: usize = a[2].parse().unwrap();
    let wait_s: f64 = a[3].parse().unwrap();
    let exe = &a[4];
    let (mut ts, mut ws, mut pr) = (vec![], vec![], vec![]);
    let mut detail = String::new();
    for i in 0..n {
        let id = format!("{label}{i}");
        let args: Vec<String> = a[5..].iter().map(|x| x.replace("{id}", &id)).collect();
        let start = Instant::now();
        let child = Command::new(exe).args(&args).spawn().expect("spawn");
        let root = child.id();
        let hwnd = loop {
            let pids: Vec<u32> = if by_pid { tree(root).iter().map(|x| x.0).collect() } else { vec![] };
            let mut f = Find { title: if by_pid { None } else { Some(format!("ieummae-{id}")) }, pids, found: std::ptr::null_mut() };
            unsafe { EnumWindows(Some(cb), &mut f as *mut _ as LPARAM) };
            if !f.found.is_null() { break f.found; }
            if start.elapsed() > Duration::from_secs(60) { eprintln!("timeout"); std::process::exit(1); }
            std::thread::sleep(Duration::from_millis(1));
        };
        let ms = start.elapsed().as_secs_f64() * 1000.0;
        // 창 소유 프로세스 기준 트리 (런처가 다른 프로세스를 띄우는 경우 대비)
        let mut owner = 0;
        unsafe { GetWindowThreadProcessId(hwnd, &mut owner) };
        std::thread::sleep(Duration::from_secs_f64(wait_s));
        let mut seen = std::collections::HashSet::new();
        let mut procs = vec![];
        for p in tree(root).into_iter().chain(tree(owner)) { if seen.insert(p.0) { procs.push(p); } }
        // WebView2 는 msedgewebview2 가 호스트 자식으로 붙음 - tree 로 포함됨
        let (mut w, mut p) = (0usize, 0usize);
        let mut parts = vec![];
        for (pid, name) in &procs {
            let (pw, pp) = mem(*pid);
            w += pw; p += pp;
            parts.push(format!("{name}:{:.0}/{:.0}", mb(pw), mb(pp)));
        }
        if i == n - 1 { detail = parts.join(" "); }
        println!("{label} #{i}: {ms:.1} ms, ws {:.1} MB, private {:.1} MB, procs {}", mb(w), mb(p), procs.len());
        ts.push(ms); ws.push(mb(w)); pr.push(mb(p));
        for (pid, _) in procs.iter().rev() {
            let _ = Command::new("taskkill").args(["/F", "/PID", &pid.to_string()]).output();
        }
        let _ = Command::new("taskkill").args(["/F", "/T", "/PID", &root.to_string()]).output();
        std::thread::sleep(Duration::from_millis(1200));
    }
    let mut s = ts.clone();
    s.sort_by(|a, b| a.partial_cmp(b).unwrap());
    println!("== {label}: show min {:.1} / median {:.1} / max {:.1} ms, ws median {:.1} MB, private median {:.1} MB (n={n})",
        s[0], median(&ts), s[s.len() - 1], median(&ws), median(&pr));
    println!("   last procs (ws/private MB): {detail}");
    println!("RESULT {{\"label\":\"{label}\",\"n\":{n},\"show_min\":{:.1},\"show_med\":{:.1},\"show_max\":{:.1},\"ws_med\":{:.1},\"priv_med\":{:.1}}}",
        s[0], median(&ts), s[s.len() - 1], median(&ws), median(&pr));
}
