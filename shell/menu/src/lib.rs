// 이음매 탐색기 우클릭 메뉴 - Win11 새 메뉴의 IExplorerCommand
// 구조: 최상위 "이음매" 하나 + 하위 명령들. 누르면 ieummae.exe <명령> <경로> 실행
// 탐색기 충돌 격리 - sparse package 의 SurrogateServer 로 dllhost 대리 프로세스에서 실행됨
#![allow(non_snake_case)]

pub mod location;

use location::{items, Item, Location};
use std::cell::Cell;
use std::ffi::c_void;
use std::path::PathBuf;
use std::sync::atomic::{AtomicIsize, Ordering};
use windows::core::{implement, Interface, Ref, BOOL, GUID, HRESULT, PCWSTR, PWSTR};
use windows::Win32::Foundation::{CLASS_E_CLASSNOTAVAILABLE, CLASS_E_NOAGGREGATION, E_FAIL, E_NOTIMPL, E_POINTER, HINSTANCE, S_FALSE, S_OK};
use windows::Win32::System::Com::{CoTaskMemAlloc, IBindCtx, IClassFactory, IClassFactory_Impl};
use windows::Win32::System::LibraryLoader::GetModuleFileNameW;
use windows::Win32::System::SystemServices::DLL_PROCESS_ATTACH;
use windows::Win32::System::Threading::{CreateProcessW, CREATE_UNICODE_ENVIRONMENT, PROCESS_INFORMATION, STARTUPINFOW};
use windows::Win32::UI::Shell::{
    IEnumExplorerCommand, IEnumExplorerCommand_Impl, IExplorerCommand, IExplorerCommand_Impl, IShellItemArray, ECF_DEFAULT,
    ECF_HASSUBCOMMANDS, ECF_ISSEPARATOR, ECS_ENABLED, ECS_HIDDEN, SIGDN_FILESYSPATH,
};

// 패키지 매니페스트의 com:Class Id 와 같아야 함
pub const CLSID_MENU: GUID = GUID::from_u128(0x4A9D1BAA_17B6_4EE0_AABB_8364E3861AF2);

static MODULE: AtomicIsize = AtomicIsize::new(0);

#[no_mangle]
extern "system" fn DllMain(instance: HINSTANCE, reason: u32, _: *mut c_void) -> BOOL {
    if reason == DLL_PROCESS_ATTACH {
        MODULE.store(instance.0 as isize, Ordering::Relaxed);
    }
    true.into()
}

// ieummae.exe - 이 DLL 과 같은 폴더 (IEUMMAE_EXE 로 바꿀 수 있음 - 확인 도구용)
fn exe_path() -> PathBuf {
    if let Ok(p) = std::env::var("IEUMMAE_EXE") {
        return PathBuf::from(p);
    }
    let mut buf = [0u16; 1024];
    let n = unsafe { GetModuleFileNameW(Some(HINSTANCE(MODULE.load(Ordering::Relaxed) as *mut c_void).into()), &mut buf) } as usize;
    let dll = PathBuf::from(String::from_utf16_lossy(&buf[..n]));
    dll.parent().map(|d| d.join("ieummae.exe")).unwrap_or_else(|| PathBuf::from("ieummae.exe"))
}

// 셸이 해제하는 문자열 - CoTaskMemAlloc 로 할당
fn co_string(s: &str) -> windows::core::Result<PWSTR> {
    let w: Vec<u16> = s.encode_utf16().chain(std::iter::once(0)).collect();
    unsafe {
        let p = CoTaskMemAlloc(w.len() * 2) as *mut u16;
        if p.is_null() {
            return Err(E_FAIL.into());
        }
        std::ptr::copy_nonoverlapping(w.as_ptr(), p, w.len());
        Ok(PWSTR(p))
    }
}

// 고른 항목 첫 번째의 파일 시스템 경로
pub fn first_path(items: Option<&IShellItemArray>) -> Option<PathBuf> {
    let items = items?;
    unsafe {
        if items.GetCount().ok()? == 0 {
            return None;
        }
        let item = items.GetItemAt(0).ok()?;
        let name = item.GetDisplayName(SIGDN_FILESYSPATH).ok()?;
        let s = name.to_string().ok();
        windows::Win32::System::Com::CoTaskMemFree(Some(name.0 as *const c_void));
        s.map(PathBuf::from)
    }
}

// ieummae.exe 실행 - 탐색기와 떨어진 독립 프로세스
pub fn launch(cmd: &str, path: &PathBuf) -> windows::core::Result<()> {
    let exe = exe_path();
    // 테스트용 - 실제 실행 대신 명령줄만 출력
    if std::env::var("IEUMMAE_MENU_DRYRUN").is_ok() {
        println!("\"{}\" {} \"{}\"", exe.display(), cmd, path.display());
        return Ok(());
    }
    let line = format!("\"{}\" {} \"{}\"", exe.display(), cmd, path.display().to_string().trim_end_matches('\\'));
    let mut line_w: Vec<u16> = line.encode_utf16().chain(std::iter::once(0)).collect();
    let exe_w: Vec<u16> = exe.as_os_str().to_string_lossy().encode_utf16().chain(std::iter::once(0)).collect();
    let dir = if path.is_dir() { path.clone() } else { path.parent().map(|p| p.to_path_buf()).unwrap_or_default() };
    let dir_w: Vec<u16> = dir.as_os_str().to_string_lossy().encode_utf16().chain(std::iter::once(0)).collect();
    let si = STARTUPINFOW { cb: std::mem::size_of::<STARTUPINFOW>() as u32, ..Default::default() };
    let mut pi = PROCESS_INFORMATION::default();
    unsafe {
        CreateProcessW(
            PCWSTR(exe_w.as_ptr()),
            Some(PWSTR(line_w.as_mut_ptr())),
            None,
            None,
            false,
            CREATE_UNICODE_ENVIRONMENT,
            None,
            PCWSTR(dir_w.as_ptr()),
            &si,
            &mut pi,
        )?;
        let _ = windows::Win32::Foundation::CloseHandle(pi.hProcess);
        let _ = windows::Win32::Foundation::CloseHandle(pi.hThread);
    }
    Ok(())
}

// 최상위 "이음매" - 하위 명령을 가진 메뉴
// 셸은 GetState(고른 항목 있음) 다음 EnumSubCommands(항목 없음) 순서로 부름 - 위치를 기억해 그 위치 항목만 돌려줌
#[implement(IExplorerCommand)]
#[derive(Default)]
pub struct Root {
    loc: std::cell::RefCell<Option<Location>>,
}

impl IExplorerCommand_Impl for Root_Impl {
    fn GetTitle(&self, _: Ref<IShellItemArray>) -> windows::core::Result<PWSTR> { co_string("이음매") }
    fn GetIcon(&self, _: Ref<IShellItemArray>) -> windows::core::Result<PWSTR> { co_string(&format!("{},0", exe_path().display())) }
    fn GetToolTip(&self, _: Ref<IShellItemArray>) -> windows::core::Result<PWSTR> { Err(E_NOTIMPL.into()) }
    fn GetCanonicalName(&self) -> windows::core::Result<GUID> { Ok(CLSID_MENU) }
    fn GetState(&self, items: Ref<IShellItemArray>, _: BOOL) -> windows::core::Result<u32> {
        // 파일 시스템 경로가 아닌 곳(제어판 등)에서는 숨김
        let loc = first_path(items.as_ref()).map(|p| Location::of(&p));
        let state = if loc.is_some() { ECS_ENABLED } else { ECS_HIDDEN };
        *self.loc.borrow_mut() = loc;
        Ok(state.0 as u32)
    }
    fn Invoke(&self, _: Ref<IShellItemArray>, _: Ref<IBindCtx>) -> windows::core::Result<()> { Err(E_NOTIMPL.into()) }
    fn GetFlags(&self) -> windows::core::Result<u32> { Ok(ECF_HASSUBCOMMANDS.0 as u32) }
    fn EnumSubCommands(&self) -> windows::core::Result<IEnumExplorerCommand> {
        // 위치를 모르면(GetState 없이 바로 불린 경우) 모든 항목, 안 맞는 것은 하위 GetState 에서 숨김
        let list = match self.loc.borrow().as_ref() { Some(loc) => items(loc), None => all_items() };
        let all: Vec<IExplorerCommand> = list.into_iter().map(|i| Sub { item: i }.into()).collect();
        Ok(SubList { items: all, pos: Cell::new(0) }.into())
    }
}

// 위치와 무관하게 나올 수 있는 모든 항목 (저장소 안·밖, 파일·폴더, 병합 중) - 순서 유지, 중복 제거
pub fn all_items() -> Vec<Item> {
    let samples = [
        Location { path: PathBuf::new(), is_file: true, repo: Some(PathBuf::new()), operation: true },
        Location { path: PathBuf::new(), is_file: false, repo: None, operation: false },
    ];
    let mut v: Vec<Item> = Vec::new();
    for loc in &samples {
        for i in items(loc) {
            if i.id.is_none() || !v.iter().any(|x| x.id == i.id) {
                v.push(i);
            }
        }
    }
    v
}

// 하위 명령 하나 - 지금 위치의 항목 목록에 있을 때만 보임
#[implement(IExplorerCommand)]
pub struct Sub {
    pub item: Item,
}

impl Sub {
    pub fn visible_at(&self, loc: &Location) -> bool {
        match self.item.id {
            Some(id) => items(loc).iter().any(|i| i.id == Some(id)),
            // 구분선 - 저장소 안에서만 (밖은 항목이 셋뿐)
            None => loc.repo.is_some(),
        }
    }
}

impl IExplorerCommand_Impl for Sub_Impl {
    fn GetTitle(&self, _: Ref<IShellItemArray>) -> windows::core::Result<PWSTR> { co_string(self.item.title) }
    fn GetIcon(&self, _: Ref<IShellItemArray>) -> windows::core::Result<PWSTR> { Err(E_NOTIMPL.into()) }
    fn GetToolTip(&self, _: Ref<IShellItemArray>) -> windows::core::Result<PWSTR> { Err(E_NOTIMPL.into()) }
    fn GetCanonicalName(&self) -> windows::core::Result<GUID> { Ok(GUID::zeroed()) }
    fn GetState(&self, items: Ref<IShellItemArray>, _: BOOL) -> windows::core::Result<u32> {
        let visible = first_path(items.as_ref()).map(|p| self.visible_at(&Location::of(&p))).unwrap_or(false);
        Ok(if visible { ECS_ENABLED.0 as u32 } else { ECS_HIDDEN.0 as u32 })
    }
    fn Invoke(&self, items: Ref<IShellItemArray>, _: Ref<IBindCtx>) -> windows::core::Result<()> {
        let (Some(id), Some(path)) = (self.item.id, first_path(items.as_ref())) else { return Ok(()) };
        launch(id, &path)
    }
    fn GetFlags(&self) -> windows::core::Result<u32> {
        Ok(if self.item.id.is_none() { ECF_ISSEPARATOR.0 as u32 } else { ECF_DEFAULT.0 as u32 })
    }
    fn EnumSubCommands(&self) -> windows::core::Result<IEnumExplorerCommand> { Err(E_NOTIMPL.into()) }
}

#[implement(IEnumExplorerCommand)]
struct SubList {
    items: Vec<IExplorerCommand>,
    pos: Cell<usize>,
}

impl IEnumExplorerCommand_Impl for SubList_Impl {
    fn Next(&self, celt: u32, out: *mut Option<IExplorerCommand>, fetched: *mut u32) -> HRESULT {
        if out.is_null() {
            return E_POINTER;
        }
        let mut n = 0u32;
        while n < celt && self.pos.get() < self.items.len() {
            unsafe { out.add(n as usize).write(Some(self.items[self.pos.get()].clone())) };
            self.pos.set(self.pos.get() + 1);
            n += 1;
        }
        if !fetched.is_null() {
            unsafe { *fetched = n };
        }
        if n == celt { S_OK } else { S_FALSE }
    }
    fn Skip(&self, celt: u32) -> windows::core::Result<()> {
        self.pos.set((self.pos.get() + celt as usize).min(self.items.len()));
        Ok(())
    }
    fn Reset(&self) -> windows::core::Result<()> {
        self.pos.set(0);
        Ok(())
    }
    fn Clone(&self) -> windows::core::Result<IEnumExplorerCommand> {
        Ok(SubList { items: self.items.clone(), pos: Cell::new(self.pos.get()) }.into())
    }
}

#[implement(IClassFactory)]
struct Factory;

impl IClassFactory_Impl for Factory_Impl {
    fn CreateInstance(&self, outer: Ref<windows::core::IUnknown>, iid: *const GUID, object: *mut *mut c_void) -> windows::core::Result<()> {
        if !outer.is_null() {
            return Err(CLASS_E_NOAGGREGATION.into());
        }
        let cmd: IExplorerCommand = Root::default().into();
        unsafe { cmd.query(iid, object).ok() }
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
        if *clsid != CLSID_MENU {
            return CLASS_E_CLASSNOTAVAILABLE;
        }
        let f: IClassFactory = Factory.into();
        f.query(iid, object)
    }
}

// 대리 프로세스가 언제든 내려도 되도록 - 상태 없음
#[no_mangle]
extern "system" fn DllCanUnloadNow() -> HRESULT { S_FALSE }
