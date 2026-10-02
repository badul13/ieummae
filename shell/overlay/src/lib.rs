// 이음매 아이콘 표시 - IShellIconOverlayIdentifier 넷 (정상·수정·충돌·추가)
// 탐색기 프로세스 안에서 실행됨 - 여기서는 git 을 돌리지 않고 캐시 프로세스에 파이프로 묻기만
// 탐색기는 파일마다 표시 넷을 차례로 물으므로, 한 번 물은 경로는 잠깐 기억해 파이프는 한 번만
#![allow(non_snake_case)]

use ieummae_common::{pipe_name, Status};
use std::collections::HashMap;
use std::ffi::c_void;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicIsize, AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use windows::core::{implement, Interface, Ref, BOOL, GUID, HRESULT, HSTRING, PCWSTR, PWSTR};
use windows::Win32::Foundation::{CLASS_E_CLASSNOTAVAILABLE, CLASS_E_NOAGGREGATION, E_POINTER, HINSTANCE, S_FALSE};
use windows::Win32::Storage::FileSystem::GetDriveTypeW;
use windows::Win32::System::Com::{IClassFactory, IClassFactory_Impl};
use windows::Win32::System::LibraryLoader::GetModuleFileNameW;
use windows::Win32::System::Pipes::CallNamedPipeW;
use windows::Win32::System::SystemServices::DLL_PROCESS_ATTACH;
use windows::Win32::System::Threading::{CreateProcessW, CREATE_NO_WINDOW, DETACHED_PROCESS, PROCESS_INFORMATION, STARTUPINFOW};
use windows::Win32::UI::Shell::{IShellIconOverlayIdentifier, IShellIconOverlayIdentifier_Impl, ISIOI_ICONFILE, ISIOI_ICONINDEX};

// 표시 종류와 CLSID - 설치 스크립트가 레지스트리에 같은 값으로 등록
pub const OVERLAYS: [(Status, GUID, &str); 4] = [
    (Status::Normal, GUID::from_u128(0x970154D1_6709_4CD0_9D88_7FF061ECC42E), "normal"),
    (Status::Modified, GUID::from_u128(0x05EC32BD_A419_4539_A6AE_9B60DDA05C95), "modified"),
    (Status::Conflict, GUID::from_u128(0x2D77E08E_C1BB_4777_991F_7F663A75BA5B), "conflict"),
    (Status::Added, GUID::from_u128(0x557BC428_426C_4932_9FFB_BF3DA931FF33), "added"),
];

// 파이프 응답 기다리는 최대 시간 - 탐색기 화면이 멈추지 않을 만큼
const PIPE_TIMEOUT_MS: u32 = 40;
// 같은 경로 다시 묻지 않는 시간 - 표시 넷이 한 번에 묻는 동안
const ANSWER_TTL: Duration = Duration::from_millis(1500);
// 캐시 프로세스 다시 띄우기 간격
const RESTART_GAP_MS: u64 = 10_000;

static MODULE: AtomicIsize = AtomicIsize::new(0);
static LAST_START: AtomicU64 = AtomicU64::new(0);
static ANSWERS: Mutex<Option<HashMap<PathBuf, (Status, Instant)>>> = Mutex::new(None);

#[no_mangle]
extern "system" fn DllMain(instance: HINSTANCE, reason: u32, _: *mut c_void) -> BOOL {
    if reason == DLL_PROCESS_ATTACH {
        MODULE.store(instance.0 as isize, Ordering::Relaxed);
    }
    true.into()
}

fn module_dir() -> PathBuf {
    let mut buf = [0u16; 1024];
    let n = unsafe { GetModuleFileNameW(Some(HINSTANCE(MODULE.load(Ordering::Relaxed) as *mut c_void).into()), &mut buf) } as usize;
    PathBuf::from(String::from_utf16_lossy(&buf[..n])).parent().map(|p| p.to_path_buf()).unwrap_or_default()
}

// 로컬 디스크만 - 네트워크 드라이브는 파일 확인 자체가 느려 탐색기를 붙잡음
fn local_drive(path: &Path) -> bool {
    let s = path.to_string_lossy();
    if s.len() < 3 || s.starts_with(r"\\") {
        return false;
    }
    let root = HSTRING::from(&s[..3]);
    // DRIVE_REMOVABLE 2, DRIVE_FIXED 3
    matches!(unsafe { GetDriveTypeW(&root) }, 2 | 3)
}

// 경로 상태 - 기억해 둔 답 → 캐시 프로세스. 파이프 없으면 프로세스 띄우고 Unknown
pub fn status_of(path: &Path) -> Status {
    if !local_drive(path) {
        return Status::Unknown;
    }
    {
        let mut g = ANSWERS.lock().unwrap();
        let map = g.get_or_insert_with(HashMap::new);
        if let Some((st, t)) = map.get(path) {
            if t.elapsed() < ANSWER_TTL {
                return *st;
            }
        }
    }
    let st = ask(path);
    let mut g = ANSWERS.lock().unwrap();
    let map = g.get_or_insert_with(HashMap::new);
    if map.len() > 4096 {
        map.clear();
    }
    map.insert(path.to_path_buf(), (st, Instant::now()));
    st
}

pub fn ask(path: &Path) -> Status {
    let req: Vec<u8> = path.as_os_str().to_string_lossy().encode_utf16().flat_map(|w| w.to_le_bytes()).collect();
    let mut out = [0u8; 1];
    let mut n = 0u32;
    let ok = unsafe {
        CallNamedPipeW(
            &HSTRING::from(pipe_name()),
            Some(req.as_ptr() as *const c_void),
            req.len() as u32,
            Some(out.as_mut_ptr() as *mut c_void),
            1,
            &mut n,
            PIPE_TIMEOUT_MS,
        )
    };
    if ok.as_bool() && n == 1 {
        return Status::from_byte(out[0]);
    }
    start_cache();
    Status::Unknown
}

// 캐시 프로세스 띄우기 - DLL 옆 ieummae-cache.exe, 10초에 한 번까지
fn start_cache() {
    let now = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_millis() as u64).unwrap_or(0);
    let last = LAST_START.load(Ordering::Relaxed);
    if now.saturating_sub(last) < RESTART_GAP_MS || LAST_START.compare_exchange(last, now, Ordering::Relaxed, Ordering::Relaxed).is_err() {
        return;
    }
    let exe = module_dir().join("ieummae-cache.exe");
    let exe_w = HSTRING::from(exe.as_os_str());
    let mut line: Vec<u16> = format!("\"{}\"", exe.display()).encode_utf16().chain(std::iter::once(0)).collect();
    let si = STARTUPINFOW { cb: std::mem::size_of::<STARTUPINFOW>() as u32, ..Default::default() };
    let mut pi = PROCESS_INFORMATION::default();
    unsafe {
        if CreateProcessW(&exe_w, Some(PWSTR(line.as_mut_ptr())), None, None, false, DETACHED_PROCESS | CREATE_NO_WINDOW, None, PCWSTR::null(), &si, &mut pi).is_ok() {
            let _ = windows::Win32::Foundation::CloseHandle(pi.hProcess);
            let _ = windows::Win32::Foundation::CloseHandle(pi.hThread);
        }
    }
}

// 표시 하나 - 상태가 같으면 그 표시
#[implement(IShellIconOverlayIdentifier)]
pub struct Overlay {
    pub index: usize,
}

impl IShellIconOverlayIdentifier_Impl for Overlay_Impl {
    fn IsMemberOf(&self, path: &PCWSTR, _attrib: u32) -> windows::core::Result<()> {
        if path.is_null() {
            return Err(S_FALSE.into());
        }
        let p = PathBuf::from(unsafe { path.to_string() }.unwrap_or_default());
        if status_of(&p) == OVERLAYS[self.index].0 { Ok(()) } else { Err(S_FALSE.into()) }
    }

    // 아이콘 파일 - DLL 옆 overlays\<종류>.ico
    fn GetOverlayInfo(&self, file: PWSTR, max: i32, index: *mut i32, flags: *mut u32) -> windows::core::Result<()> {
        let ico = module_dir().join("overlays").join(format!("{}.ico", OVERLAYS[self.index].2));
        let w: Vec<u16> = ico.as_os_str().to_string_lossy().encode_utf16().chain(std::iter::once(0)).collect();
        if file.is_null() || w.len() > max as usize {
            return Err(E_POINTER.into());
        }
        unsafe {
            std::ptr::copy_nonoverlapping(w.as_ptr(), file.0, w.len());
            if !index.is_null() { *index = 0; }
            if !flags.is_null() { *flags = ISIOI_ICONFILE | ISIOI_ICONINDEX; }
        }
        Ok(())
    }

    // 우선순위 - 충돌이 가장 먼저 (0 이 가장 높음)
    fn GetPriority(&self) -> windows::core::Result<i32> {
        Ok(match OVERLAYS[self.index].0 { Status::Conflict => 0, Status::Modified => 10, Status::Added => 20, _ => 30 })
    }
}

#[implement(IClassFactory)]
struct Factory {
    index: usize,
}

impl IClassFactory_Impl for Factory_Impl {
    fn CreateInstance(&self, outer: Ref<windows::core::IUnknown>, iid: *const GUID, object: *mut *mut c_void) -> windows::core::Result<()> {
        if !outer.is_null() {
            return Err(CLASS_E_NOAGGREGATION.into());
        }
        let o: IShellIconOverlayIdentifier = Overlay { index: self.index }.into();
        unsafe { o.query(iid, object).ok() }
    }
    fn LockServer(&self, _: BOOL) -> windows::core::Result<()> { Ok(()) }
}

#[no_mangle]
extern "system" fn DllGetClassObject(clsid: *const GUID, iid: *const GUID, object: *mut *mut c_void) -> HRESULT {
    if clsid.is_null() || iid.is_null() || object.is_null() {
        return E_POINTER;
    }
    unsafe {
        *object = std::ptr::null_mut();
        let Some(index) = OVERLAYS.iter().position(|o| o.1 == *clsid) else { return CLASS_E_CLASSNOTAVAILABLE };
        let f: IClassFactory = Factory { index }.into();
        f.query(iid, object)
    }
}

#[no_mangle]
extern "system" fn DllCanUnloadNow() -> HRESULT { S_FALSE }
