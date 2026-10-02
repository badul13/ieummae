// 아이콘 표시 확인 도구 - 탐색기 없이 캐시 프로세스와 파이프로 주고받기
// 임시 저장소를 만들고: 처음 응답(Unknown) → 계산 뒤 상태 → 파일 고치면 바뀌는지
use ieummae_common::Status;
use ieummae_overlay::ask;
use std::path::Path;
use std::process::Command;
use std::time::{Duration, Instant};

fn git(dir: &Path, args: &[&str]) {
    let ok = Command::new("git").arg("-C").arg(dir).args(args).output().unwrap().status.success();
    assert!(ok, "git {args:?}");
}

// 기대 상태가 될 때까지 (최대 5초)
fn wait(path: &Path, want: Status) -> Duration {
    let t = Instant::now();
    loop {
        let st = ask(path);
        if st == want {
            return t.elapsed();
        }
        assert!(t.elapsed() < Duration::from_secs(5), "{} : {:?} 기다리다 {:?}", path.display(), want, st);
        std::thread::sleep(Duration::from_millis(50));
    }
}

fn main() {
    let cache = std::env::current_exe().unwrap().parent().unwrap().parent().unwrap().join("ieummae-cache.exe");
    let mut child = Command::new(&cache).spawn().expect("캐시 프로세스");
    std::thread::sleep(Duration::from_millis(200));

    let repo = std::env::temp_dir().join(format!("ieummae-overlay-probe-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&repo);
    std::fs::create_dir_all(repo.join("src")).unwrap();
    git(&repo, &["init", "-q", "-b", "main"]);
    git(&repo, &["config", "user.name", "t"]);
    git(&repo, &["config", "user.email", "t@x"]);
    std::fs::write(repo.join("src").join("a.txt"), "1").unwrap();
    std::fs::write(repo.join(".gitignore"), "bin/\n").unwrap();
    git(&repo, &["add", "."]);
    git(&repo, &["commit", "-q", "-m", "첫"]);
    std::fs::create_dir_all(repo.join("bin")).unwrap();
    std::fs::write(repo.join("bin").join("x.dll"), "x").unwrap();

    let a = repo.join("src").join("a.txt");
    let first = ask(&a);
    println!("처음 응답: {first:?} (아직 계산 전이면 Unknown)");
    println!("정상 표시까지: {:?}", wait(&a, Status::Normal));
    println!("폴더 src: {:?}", ask(&repo.join("src")));
    println!("무시된 bin: {:?}", ask(&repo.join("bin")));

    std::fs::write(&a, "2").unwrap();
    println!("고친 뒤 수정 표시까지: {:?}", wait(&a, Status::Modified));
    println!("폴더 src: {:?}, 저장소 폴더: {:?}", ask(&repo.join("src")), ask(&repo));

    std::fs::write(repo.join("src").join("b.txt"), "n").unwrap();
    git(&repo, &["add", "src/b.txt"]);
    println!("새로 스테이징 → 추가 표시까지: {:?}", wait(&repo.join("src").join("b.txt"), Status::Added));

    // 응답 속도 - 계산된 뒤 파이프 왕복
    let t = Instant::now();
    for _ in 0..200 {
        ask(&a);
    }
    println!("파이프 왕복 평균: {:?}", t.elapsed() / 200);

    let _ = child.kill();
    let _ = std::fs::remove_dir_all(&repo);
}
