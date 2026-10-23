// 맨 앞 탐색기 창의 현재 폴더 - 폴더 빈 곳 우클릭(새 메뉴)은 GetState 에 항목 없이 불리므로
// 셸 창 목록(IShellWindows)에서 맨 앞 창을 찾아 그 위치 (Windows Terminal "터미널에서 열기"와 같은 방식)
// Win11 탭 - 한 창에 탭마다 항목이 따로, 보고 있는 탭은 z 순서 맨 위 ShellTabWindowClass
use std::path::PathBuf;
use windows::core::{w, Interface, PWSTR};
use windows::Win32::Foundation::HWND;
use windows::Win32::System::Com::{CoCreateInstance, IServiceProvider, CLSCTX_ALL};
use windows::Win32::System::Variant::VARIANT;
use windows::Win32::UI::Shell::{IShellBrowser, IShellWindows, IWebBrowserApp, PathCreateFromUrlW, ShellWindows, SID_STopLevelBrowser};
use windows::Win32::UI::WindowsAndMessaging::{FindWindowExW, GetClassNameW, GetForegroundWindow};

pub fn current_folder() -> Option<PathBuf> {
    unsafe {
        let top = GetForegroundWindow();
        let mut class = [0u16; 64];
        let n = GetClassNameW(top, &mut class) as usize;
        if String::from_utf16_lossy(&class[..n]) != "CabinetWClass" {
            return None;
        }
        let active_tab = FindWindowExW(Some(top), None, w!("ShellTabWindowClass"), None).ok();
        let windows: IShellWindows = CoCreateInstance(&ShellWindows, None, CLSCTX_ALL).ok()?;
        let mut first = None;
        for i in 0..windows.Count().ok()? {
            let Ok(disp) = windows.Item(&VARIANT::from(i)) else { continue };
            let Ok(app) = disp.cast::<IWebBrowserApp>() else { continue };
            if app.HWND().ok().map(|h| h.0 as isize) != Some(top.0 as isize) {
                continue;
            }
            // 탭 창을 못 얻으면(다른 프로세스라 막힌 경우 등) 같은 창의 첫 항목
            match (tab_window(&app), active_tab) {
                (Some(tab), Some(active)) if tab == active => return location(&app),
                _ => {
                    if first.is_none() {
                        first = Some(app);
                    }
                }
            }
        }
        first.and_then(|app| location(&app))
    }
}

// 항목의 탭 창 - SID_STopLevelBrowser 의 IShellBrowser
fn tab_window(app: &IWebBrowserApp) -> Option<HWND> {
    unsafe {
        let sp: IServiceProvider = app.cast().ok()?;
        let browser: IShellBrowser = sp.QueryService(&SID_STopLevelBrowser).ok()?;
        browser.GetWindow().ok()
    }
}

// file:/// 주소 → 파일 시스템 경로, 이 PC·휴지통 같은 곳은 None
fn location(app: &IWebBrowserApp) -> Option<PathBuf> {
    unsafe {
        let url = app.LocationURL().ok()?.to_string();
        if !url.starts_with("file:") {
            return None;
        }
        let url_w: Vec<u16> = url.encode_utf16().chain(std::iter::once(0)).collect();
        let mut buf = [0u16; 1024];
        let mut len = buf.len() as u32;
        PathCreateFromUrlW(windows::core::PCWSTR(url_w.as_ptr()), PWSTR(buf.as_mut_ptr()), &mut len, 0).ok()?;
        Some(PathBuf::from(String::from_utf16_lossy(&buf[..len as usize])))
    }
}
