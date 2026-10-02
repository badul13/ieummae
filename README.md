# ieummae

Windows 탐색기 우클릭에서 바로 쓰는 Git 도구.

## 현재 상태

1단계 뼈대. 로그·커밋 창의 틀과 테마까지, git 기능은 다음 단계부터. UI 스택은 Avalonia 12 + NativeAOT ([결정 기록](docs/STACK.md)).

## 구조

| 경로 | 내용 |
|---|---|
| `src/Ieummae.Core` | git CLI 실행, 저장소 찾기, 실행 인자. UI 의존 없음 |
| `src/Ieummae.App` | 창 전부. 단일 exe `ieummae <명령> [경로]` |
| `tests/Ieummae.Core.Tests` | 임시 저장소를 실제로 만들어 git 동작 검증 |
| `tools/preview` | 창을 띄우지 않고 화면을 PNG 로 그려 브라우저에서 확인 |
| `tools/bench` | 창 표시 시간·메모리 측정 (Rust) |
| `design/mockup` | 디자인 시안 (참고용) |

## 개발

```powershell
dotnet build Ieummae.slnx
dotnet test tests/Ieummae.Core.Tests

# 화면 미리보기 - tools/preview/out/index.html 을 브라우저로 열어 두면 다시 실행할 때마다 갱신
dotnet run --project tools/preview -- <저장소 경로>

# 배포본 - out/app/ieummae.exe
./scripts/publish.ps1
out/app/ieummae.exe log <저장소 경로> [--theme light|dark]
```

## 라이선스

[MIT](LICENSE). 글꼴은 각 라이선스(`src/Ieummae.App/Assets/Fonts/licenses`).
