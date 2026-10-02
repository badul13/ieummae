// 메뉴 확인 도구 - 탐색기 대신 DLL 을 COM 으로 불러 경로별로 보이는 항목 출력
// 사용: cargo run --release --example probe -- <경로>...  (마지막에 --invoke <제목> 이면 그 항목 실행 흉내)
use std::ffi::c_void;
use windows::core::{Interface, GUID, HRESULT, HSTRING, PCSTR};
use windows::Win32::System::Com::{CoInitializeEx, IClassFactory, COINIT_APARTMENTTHREADED};
use windows::Win32::System::LibraryLoader::{GetProcAddress, LoadLibraryW};
use windows::Win32::UI::Shell::{IExplorerCommand, IShellItem, IShellItemArray, SHCreateItemFromParsingName, SHCreateShellItemArrayFromShellItem, ECS_HIDDEN};

type GetClassObject = unsafe extern "system" fn(*const GUID, *const GUID, *mut *mut c_void) -> HRESULT;

fn main() -> windows::core::Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let (paths, invoke) = match args.iter().position(|a| a == "--invoke") {
        Some(i) => (args[..i].to_vec(), args.get(i + 1).cloned()),
        None => (args.clone(), None),
    };
    unsafe {
        CoInitializeEx(None, COINIT_APARTMENTTHREADED).ok()?;
        // 빌드된 DLL 을 실제로 불러옴 - 탐색기(대리 프로세스)와 같은 경로
        let dll = std::env::current_exe().unwrap().parent().unwrap().parent().unwrap().join("ieummae_menu.dll");
        let module = LoadLibraryW(&HSTRING::from(dll.as_os_str()))?;
        let get: GetClassObject = std::mem::transmute(GetProcAddress(module, PCSTR(c"DllGetClassObject".as_ptr() as _)).expect("DllGetClassObject"));
        let mut factory: *mut c_void = std::ptr::null_mut();
        get(&ieummae_menu::CLSID_MENU, &IClassFactory::IID, &mut factory).ok()?;
        let factory = IClassFactory::from_raw(factory);
        let root: IExplorerCommand = factory.CreateInstance(None)?;

        for p in &paths {
            let item: IShellItem = SHCreateItemFromParsingName(&HSTRING::from(p.as_str()), None)?;
            let array: IShellItemArray = SHCreateShellItemArrayFromShellItem(&item)?;
            println!("== {p}");
            // 탐색기 순서 - 최상위 상태 확인 다음 하위 명령 열거
            root.GetState(&array, false.into())?;
            println!("  [{}]", root.GetTitle(&array)?.to_string().unwrap());
            let subs = root.EnumSubCommands()?;
            let mut buf = [None];
            let mut line = Vec::new();
            loop {
                let mut n = 0;
                let _ = subs.Next(&mut buf, Some(&mut n));
                if n == 0 { break; }
                let cmd = buf[0].take().unwrap();
                if cmd.GetState(&array, false.into())? == ECS_HIDDEN.0 as u32 { continue; }
                let title = if cmd.GetFlags()? & 8 != 0 { "|".to_string() } else { cmd.GetTitle(&array)?.to_string().unwrap() };
                if invoke.as_deref() == Some(title.as_str()) {
                    std::env::set_var("IEUMMAE_MENU_DRYRUN", "1");
                    cmd.Invoke(&array, None)?;
                }
                line.push(title);
            }
            println!("  {}", line.join(" "));
        }
    }
    Ok(())
}
