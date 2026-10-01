# UI 스택 결정

**Avalonia 12 + C# (.NET 10) + NativeAOT + 소프트웨어 렌더링** (2026-10-01 확정)

## 이유

- 속도·메모리 - 창 표시 64ms, 전용 메모리 17MB. TortoiseGit(69ms, 6MB)과 같은 급
- 화면 구성 - 표·트리·메뉴 등 기본 컨트롤 완비. 로그·커밋·diff 창 구현 부담 적음
- 자기만의 톤 - 컨트롤 템플릿 통째 교체, 셀렉터 스타일, 커스텀 타이틀바 공식 지원
- 언어 - C#, Java와 구조 유사
- 라이선스 - MIT

## 측정 결과

같은 가짜 로그 창(300행, 한글)으로 비교. 프로세스 시작부터 창 표시까지, 표시 3초 뒤 프로세스 트리 전용 메모리. 10회 중앙값.

| 스택 | 창 표시 | 메모리 |
|---|---|---|
| **Avalonia 12 (소프트웨어 렌더링)** | **64ms** | **17MB** |
| Avalonia 12 (GPU 렌더링) | 119ms | 96MB |
| Slint (소프트웨어 렌더링) | 54ms | 8MB |
| TortoiseGit | 69ms | 6MB |
| Tauri 2 + React | 477ms | 199MB |

- 측정 환경 - Win11, Intel Iris Xe 내장 GPU, 화면 배율 100%
- 빈 창 깜빡임 - 없음. 창이 보이는 첫 프레임부터 내용 표시 (화면 합성 결과 연속 캡처로 확인)
- Avalonia 11.3과 12.1 차이 - 측정 오차 범위

## 탈락 후보

| 후보 | 탈락 이유 |
|---|---|
| Slint | 가장 가벼움. 기본 위젯 부족, 생태계 작음, 라이선스(GPL/로열티 프리/상용) 검토 필요 |
| Tauri 2 | WebView2 기본 비용. 절감 조치 후에도 106~120MB |
| WinUI 3 | NativeAOT에서도 91MB |
| WinForms | 7MB로 가볍지만 화면 꾸미기 한계 |
| WPF | 216MB |
| gpui, egui, iced | GPU 렌더러 메모리 80~370MB, 문서·API 안정성 부족 |
| Uno, Flutter | 1초 이상 표시, 배포 크기 큼 |

## 설계에 주는 영향

- 상주 호스트 불필요 - 명령마다 새 프로세스로 충분히 빠름 (Tauri 때의 숨김 창 풀·네임드 파이프 구조 폐기)
- 소프트웨어 렌더링 고정 - `Win32PlatformOptions.RenderingMode = [Software]`. GPU 렌더링은 메모리 약 80MB 추가
- 그림자·블러 등 무거운 효과 - CPU 비용, 사용 최소화

## 주의 사항

- NativeAOT - 리플렉션 제약, 컴파일된 바인딩 기본 사용 (`AvaloniaUseCompiledBindingsByDefault`)
- NativeAOT 빌드 - ILCompiler가 PATH에서 `vswhere.exe` 탐색. VS Installer 폴더를 프로세스 PATH에 추가 필요
- 맑은 고딕 - `\`가 `₩`로 표시. 경로 표시에는 다른 글꼴 지정
