// 빈 창 깜빡임 검사 - 창이 보이는 순간부터 화면 합성 결과를 연속 캡처
// 사용: flash <라벨> <exe> [인자...]   ({id} 치환, 제목 "ieummae-{id}")
// 출력: 프레임별 경과 ms / 어두운 픽셀 비율 / 색 가짓수, 변화 시점 프레임은 BMP 저장
use std::process::Command;
use std::time::{Duration, Instant};
use windows_sys::Win32::Foundation::{BOOL, HWND, LPARAM, POINT, RECT};
use windows_sys::Win32::Graphics::Gdi::*;
use windows_sys::Win32::UI::WindowsAndMessaging::*;

struct Find { title: String, found: HWND }

unsafe extern "system" fn cb(h: HWND, l: LPARAM) -> BOOL {
    let f = &mut *(l as *mut Find);
    if IsWindowVisible(h) == 0 { return 1; }
    let mut buf = [0u16; 256];
    let n = GetWindowTextW(h, buf.as_mut_ptr(), 256);
    if n > 0 && String::from_utf16_lossy(&buf[..n as usize]) == f.title { f.found = h; return 0; }
    1
}

// 화면 DC 에서 영역 복사 - DWM 합성 결과 그대로
unsafe fn grab(x: i32, y: i32, w: i32, h: i32) -> Vec<u8> {
    let scr = GetDC(std::ptr::null_mut());
    let mem = CreateCompatibleDC(scr);
    let bmp = CreateCompatibleBitmap(scr, w, h);
    let old = SelectObject(mem, bmp);
    BitBlt(mem, 0, 0, w, h, scr, x, y, SRCCOPY);
    let mut bi: BITMAPINFO = std::mem::zeroed();
    bi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
    bi.bmiHeader.biWidth = w;
    bi.bmiHeader.biHeight = -h;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    let mut px = vec![0u8; (w * h * 4) as usize];
    GetDIBits(mem, bmp, 0, h as u32, px.as_mut_ptr() as _, &mut bi, DIB_RGB_COLORS);
    SelectObject(mem, old);
    DeleteObject(bmp);
    DeleteDC(mem);
    ReleaseDC(std::ptr::null_mut(), scr);
    px
}

// (어두운 픽셀 %, 표본 색 가짓수)
fn stats(px: &[u8]) -> (f64, usize) {
    let mut dark = 0usize;
    let mut colors = std::collections::HashSet::new();
    let n = px.len() / 4;
    for i in 0..n {
        let (b, g, r) = (px[i * 4] as u32, px[i * 4 + 1] as u32, px[i * 4 + 2] as u32);
        if (r * 299 + g * 587 + b * 114) / 1000 < 128 { dark += 1; }
        if i % 7 == 0 && colors.len() < 1000 { colors.insert((r, g, b)); }
    }
    (dark as f64 * 100.0 / n as f64, colors.len())
}

fn save_bmp(path: &str, px: &[u8], w: i32, h: i32) {
    let mut f = Vec::new();
    let size = 54 + px.len() as u32;
    f.extend_from_slice(b"BM");
    f.extend_from_slice(&size.to_le_bytes());
    f.extend_from_slice(&[0, 0, 0, 0]);
    f.extend_from_slice(&54u32.to_le_bytes());
    f.extend_from_slice(&40u32.to_le_bytes());
    f.extend_from_slice(&w.to_le_bytes());
    f.extend_from_slice(&(-h).to_le_bytes());
    f.extend_from_slice(&1u16.to_le_bytes());
    f.extend_from_slice(&32u16.to_le_bytes());
    f.extend_from_slice(&[0u8; 24]);
    f.extend_from_slice(px);
    std::fs::write(path, f).unwrap();
}

fn main() {
    let a: Vec<String> = std::env::args().collect();
    let label = &a[1];
    let id = format!("{label}f");
    let args: Vec<String> = a[3..].iter().map(|x| x.replace("{id}", &id)).collect();
    let out = std::env::current_exe().unwrap().parent().unwrap().join("flash-out");
    std::fs::create_dir_all(&out).unwrap();
    // 검정 배경막 - 뒤쪽 사용자 화면이 찍히지 않게 + 빈 창/내용 구분 쉽게
    unsafe {
        let cls: Vec<u16> = "ieumBackdrop\0".encode_utf16().collect();
        let mut wc: WNDCLASSW = std::mem::zeroed();
        wc.lpfnWndProc = Some(DefWindowProcW);
        wc.lpszClassName = cls.as_ptr();
        wc.hbrBackground = GetStockObject(BLACK_BRUSH) as _;
        RegisterClassW(&wc);
        let (sw, sh) = (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
        let bd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls.as_ptr(), cls.as_ptr(), WS_POPUP,
            0, 0, sw, sh, std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null_mut(), std::ptr::null());
        ShowWindow(bd, SW_SHOWNOACTIVATE);
        SetWindowPos(bd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        UpdateWindow(bd);
        let t = Instant::now();
        let mut msg: MSG = std::mem::zeroed();
        while t.elapsed() < Duration::from_millis(400) {
            while PeekMessageW(&mut msg, std::ptr::null_mut(), 0, 0, PM_REMOVE) != 0 { DispatchMessageW(&msg); }
            std::thread::sleep(Duration::from_millis(5));
        }
    }
    let start = Instant::now();
    let mut child = Command::new(&a[2]).args(&args).spawn().unwrap();
    let hwnd = loop {
        let mut f = Find { title: format!("ieummae-{id}"), found: std::ptr::null_mut() };
        unsafe { EnumWindows(Some(cb), &mut f as *mut _ as LPARAM) };
        if !f.found.is_null() { break f.found; }
        if start.elapsed() > Duration::from_secs(30) { panic!("timeout"); }
        std::thread::yield_now();
    };
    let t0 = Instant::now();
    // 다른 창에 가려지지 않게 즉시 최상위로 (활성화는 안 함)
    unsafe { SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE) };
    println!("{label}: 창 보임 {:.1} ms (프로세스 시작 기준)", start.elapsed().as_secs_f64() * 1000.0);
    let mut last: Option<(f64, usize)> = None;
    let mut saved = 0;
    while t0.elapsed() < Duration::from_millis(600) {
        let (mut rc, mut pt): (RECT, POINT) = unsafe { std::mem::zeroed() };
        unsafe { GetClientRect(hwnd, &mut rc); ClientToScreen(hwnd, &mut pt); }
        let (w, h) = (rc.right - rc.left, rc.bottom - rc.top);
        if w <= 0 || h <= 0 { continue; }
        unsafe { SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE) };
        let px = unsafe { grab(pt.x, pt.y, w, h) };
        let ms = t0.elapsed().as_secs_f64() * 1000.0;
        let s = stats(&px);
        let changed = match last { None => true, Some(l) => (l.0 - s.0).abs() > 0.3 || (l.1 as i64 - s.1 as i64).abs() > 20 };
        if changed {
            println!("  +{ms:6.1} ms  어두운 픽셀 {:5.2}%  색 {:4}", s.0, s.1);
            if saved < 6 { save_bmp(out.join(format!("{label}-{saved}-{ms:.0}ms.bmp")).to_str().unwrap(), &px, w, h); saved += 1; }
            last = Some(s);
        }
    }
    let _ = child.kill();
    let _ = Command::new("taskkill").args(["/F", "/T", "/PID", &child.id().to_string()]).output();
}
