// 상태 캐시 프로세스 - 오버레이 DLL(탐색기 안)이 git 을 직접 돌리지 않도록 대신 계산해 둠
// - 이름 있는 파이프로 "경로 → 상태 한 바이트" 응답. 아직 모르는 저장소면 Unknown 주고 백그라운드로 계산
// - 저장소 폴더 변경 감시(ReadDirectoryChangesW) → 0.3초 모아서 다시 계산 → 바뀐 항목만 탐색기에 알림
// - 사용자당 하나만 실행, 2시간 요청 없으면 종료
#![windows_subsystem = "windows"]

use ieummae_common::{key, pipe_name, RepoStatus, Status};
use std::collections::{HashMap, HashSet};
use std::os::windows::process::CommandExt;
use std::path::{Path, PathBuf};
use std::sync::mpsc::{channel, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use windows::core::HSTRING;
use windows::Win32::Foundation::{CloseHandle, HANDLE, INVALID_HANDLE_VALUE};
use windows::Win32::Storage::FileSystem::{
    CreateFileW, ReadDirectoryChangesW, ReadFile, WriteFile, FILE_FLAG_BACKUP_SEMANTICS, FILE_FLAG_FIRST_PIPE_INSTANCE, FILE_LIST_DIRECTORY,
    FILE_NOTIFY_CHANGE_DIR_NAME, FILE_NOTIFY_CHANGE_FILE_NAME, FILE_NOTIFY_CHANGE_LAST_WRITE, FILE_NOTIFY_CHANGE_SIZE, FILE_NOTIFY_INFORMATION,
    FILE_SHARE_DELETE, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING, PIPE_ACCESS_DUPLEX,
};
use windows::Win32::System::Pipes::{ConnectNamedPipe, CreateNamedPipeW, DisconnectNamedPipe, PIPE_READMODE_MESSAGE, PIPE_TYPE_MESSAGE, PIPE_UNLIMITED_INSTANCES, PIPE_WAIT};
use windows::Win32::UI::Shell::{SHChangeNotify, SHCNE_UPDATEDIR, SHCNE_UPDATEITEM, SHCNF_FLUSHNOWAIT, SHCNF_PATHW};

const CREATE_NO_WINDOW: u32 = 0x0800_0000;
const IDLE_EXIT: Duration = Duration::from_secs(2 * 60 * 60);
const DEBOUNCE: Duration = Duration::from_millis(300);
// 한 번에 알릴 항목 수 - 넘으면 폴더 단위로
const NOTIFY_LIMIT: usize = 400;

#[derive(Default)]
struct State {
    repos: HashMap<PathBuf, RepoStatus>,
    watched: HashSet<PathBuf>,
    last_request: Option<Instant>,
}

type Shared = Arc<Mutex<State>>;

fn main() {
    let state: Shared = Arc::new(Mutex::new(State { last_request: Some(Instant::now()), ..Default::default() }));
    let (tx, rx) = channel::<PathBuf>();

    // 다시 계산 - 요청을 모아 저장소별로 0.3초 뒤 한 번
    {
        let state = state.clone();
        std::thread::spawn(move || {
            let mut due: HashMap<PathBuf, Instant> = HashMap::new();
            loop {
                let wait = due.values().min().map(|t| t.saturating_duration_since(Instant::now())).unwrap_or(Duration::from_secs(60));
                if let Ok(root) = rx.recv_timeout(wait) {
                    due.entry(root).or_insert_with(|| Instant::now() + DEBOUNCE);
                    continue;
                }
                let now = Instant::now();
                let ready: Vec<PathBuf> = due.iter().filter(|(_, t)| **t <= now).map(|(r, _)| r.clone()).collect();
                for root in ready {
                    due.remove(&root);
                    refresh(&state, &root);
                }
            }
        });
    }

    // 오래 쓰지 않으면 종료 - 다음 요청 때 오버레이 DLL 이 다시 띄움
    {
        let state = state.clone();
        std::thread::spawn(move || loop {
            std::thread::sleep(Duration::from_secs(60));
            let idle = state.lock().unwrap().last_request.map(|t| t.elapsed() > IDLE_EXIT).unwrap_or(true);
            if idle {
                std::process::exit(0);
            }
        });
    }

    serve(state, tx);
}

// 파이프 서버 - 연결마다 스레드, 첫 인스턴스 만들기 실패면 이미 실행 중
fn serve(state: Shared, tx: Sender<PathBuf>) {
    let name = HSTRING::from(pipe_name());
    let mut first = true;
    loop {
        let mode = PIPE_ACCESS_DUPLEX | if first { FILE_FLAG_FIRST_PIPE_INSTANCE } else { Default::default() };
        let pipe = unsafe { CreateNamedPipeW(&name, mode, PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT, PIPE_UNLIMITED_INSTANCES, 1024, 1024, 0, None) };
        if pipe == INVALID_HANDLE_VALUE {
            if first {
                return;
            }
            std::thread::sleep(Duration::from_millis(50));
            continue;
        }
        first = false;
        if unsafe { ConnectNamedPipe(pipe, None) }.is_err() {
            // 연결 전에 클라이언트가 이미 붙은 경우(ERROR_PIPE_CONNECTED)도 여기로 - 그대로 처리
        }
        let pipe_raw = pipe.0 as isize;
        let state = state.clone();
        let tx = tx.clone();
        std::thread::spawn(move || {
            let pipe = HANDLE(pipe_raw as *mut _);
            let mut buf = [0u8; 2048];
            let mut n = 0u32;
            if unsafe { ReadFile(pipe, Some(&mut buf), Some(&mut n), None) }.is_ok() {
                let words: Vec<u16> = buf[..n as usize].chunks_exact(2).map(|c| u16::from_le_bytes([c[0], c[1]])).collect();
                let path = PathBuf::from(String::from_utf16_lossy(&words));
                let st = answer(&state, &tx, &path) as u8;
                let mut written = 0u32;
                let _ = unsafe { WriteFile(pipe, Some(&[st]), Some(&mut written), None) };
            }
            unsafe {
                let _ = DisconnectNamedPipe(pipe);
                let _ = CloseHandle(pipe);
            }
        });
    }
}

// 경로 하나의 상태 - 저장소 처음이면 감시 시작·계산 예약하고 Unknown
fn answer(state: &Shared, tx: &Sender<PathBuf>, path: &Path) -> Status {
    let Some(root) = repo_root(path) else { return Status::Unknown };
    let is_dir = path.is_dir();
    let rel = path.strip_prefix(&root).map(|r| r.to_string_lossy().to_string()).unwrap_or_default();
    let mut s = state.lock().unwrap();
    s.last_request = Some(Instant::now());
    if let Some(repo) = s.repos.get(&root) {
        return repo.get(&rel, is_dir);
    }
    if s.watched.insert(root.clone()) {
        let _ = tx.send(root.clone());
        watch(root, tx.clone(), state.clone());
    }
    Status::Unknown
}

fn repo_root(path: &Path) -> Option<PathBuf> {
    let mut d = if path.is_dir() { Some(path) } else { path.parent() };
    while let Some(dir) = d {
        if dir.join(".git").exists() {
            return Some(dir.to_path_buf());
        }
        d = dir.parent();
    }
    None
}

// git 두 번 - 추적 파일 목록, 상태(추적 안 함 하나씩·무시 폴더 통째로)
fn refresh(state: &Shared, root: &Path) {
    let git = |args: &[&str]| -> Option<String> {
        let out = std::process::Command::new("git")
            .arg("-C").arg(root)
            .args(["-c", "core.quotepath=false"])
            .args(args)
            .env("GIT_OPTIONAL_LOCKS", "0")
            .creation_flags(CREATE_NO_WINDOW)
            .output()
            .ok()?;
        out.status.success().then(|| String::from_utf8_lossy(&out.stdout).into_owned())
    };
    let (Some(ls), Some(st)) = (git(&["ls-files", "-z"]), git(&["status", "--porcelain=v2", "-z", "-uall", "--ignored=matching"])) else { return };
    let fresh = RepoStatus::build(&ls, &st);

    // 바뀐 항목 - 탐색기가 그 아이콘만 다시 묻게
    let changed: Vec<String> = {
        let mut s = state.lock().unwrap();
        let changed = match s.repos.get(&root.to_path_buf()) {
            Some(old) => diff(old, &fresh),
            None => fresh.dirs.keys().chain(fresh.files.keys()).cloned().collect(),
        };
        s.repos.insert(root.to_path_buf(), fresh);
        changed
    };
    notify(root, &changed);
}

fn diff(old: &RepoStatus, new: &RepoStatus) -> Vec<String> {
    let mut v = Vec::new();
    for (map_old, map_new) in [(&old.files, &new.files), (&old.dirs, &new.dirs)] {
        for (k, st) in map_new {
            if map_old.get(k) != Some(st) {
                v.push(k.clone());
            }
        }
        for k in map_old.keys() {
            if !map_new.contains_key(k) {
                v.push(k.clone());
            }
        }
    }
    v
}

fn notify(root: &Path, changed: &[String]) {
    let send = |event, p: &Path| {
        let w = HSTRING::from(p.as_os_str());
        unsafe { SHChangeNotify(event, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, Some(w.as_ptr() as *const _), None) };
    };
    if changed.len() <= NOTIFY_LIMIT {
        for k in changed {
            send(SHCNE_UPDATEITEM, &root.join(k));
        }
    } else {
        // 너무 많으면 바뀐 항목들의 부모 폴더 단위로
        let dirs: HashSet<PathBuf> = changed.iter().map(|k| root.join(k).parent().map(|p| p.to_path_buf()).unwrap_or(root.to_path_buf())).collect();
        for d in dirs.iter().take(NOTIFY_LIMIT) {
            send(SHCNE_UPDATEDIR, d);
        }
    }
}

// 저장소 폴더 감시 - 바뀌면 다시 계산 예약. .git 안 객체·기록, 무시된 폴더(빌드 결과물) 변경은 무시
fn watch(root: PathBuf, tx: Sender<PathBuf>, state: Shared) {
    std::thread::spawn(move || unsafe {
        let Ok(dir) = CreateFileW(
            &HSTRING::from(root.as_os_str()),
            FILE_LIST_DIRECTORY.0,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            None,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            None,
        ) else { return };
        let mut buf = vec![0u8; 64 * 1024];
        loop {
            let mut n = 0u32;
            let ok = ReadDirectoryChangesW(
                dir,
                buf.as_mut_ptr() as *mut _,
                buf.len() as u32,
                true,
                FILE_NOTIFY_CHANGE_FILE_NAME | FILE_NOTIFY_CHANGE_DIR_NAME | FILE_NOTIFY_CHANGE_LAST_WRITE | FILE_NOTIFY_CHANGE_SIZE,
                Some(&mut n),
                None,
                None,
            );
            if ok.is_err() {
                break;
            }
            // 버퍼 넘침(n == 0) - 무엇이 바뀌었는지 모르니 다시 계산
            if n == 0 || changes(&buf[..n as usize]).iter().any(|p| relevant(&state, &root, p)) {
                let _ = tx.send(root.clone());
            }
        }
        let _ = CloseHandle(dir);
    });
}

fn changes(buf: &[u8]) -> Vec<String> {
    let mut v = Vec::new();
    let mut off = 0usize;
    loop {
        let info = unsafe { &*(buf.as_ptr().add(off) as *const FILE_NOTIFY_INFORMATION) };
        let len = info.FileNameLength as usize / 2;
        let name = unsafe { std::slice::from_raw_parts(info.FileName.as_ptr(), len) };
        v.push(String::from_utf16_lossy(name));
        if info.NextEntryOffset == 0 {
            break;
        }
        off += info.NextEntryOffset as usize;
    }
    v
}

fn relevant(state: &Shared, root: &Path, rel: &str) -> bool {
    let k = key(rel);
    if k.starts_with(".git/") {
        // 인덱스·HEAD·참조·병합 표시만 의미 있음
        return !(k.starts_with(".git/objects") || k.starts_with(".git/logs") || k.starts_with(".git/lfs") || k.ends_with(".lock"));
    }
    let s = state.lock().unwrap();
    match s.repos.get(&root.to_path_buf()) {
        Some(repo) => repo.get(&k, false) != Status::Ignored,
        None => true,
    }
}
