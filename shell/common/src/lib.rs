// 캐시 프로세스와 오버레이 DLL 공통 - 상태 값, 파이프 이름, git 출력으로 파일·폴더 상태 계산
use std::collections::HashMap;

// 아이콘 표시 상태 - 숫자는 파이프 응답 바이트
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
#[repr(u8)]
pub enum Status {
    // 모름 (아직 조회 전·저장소 밖) - 표시 없음
    Unknown = 0,
    Ignored = 1,
    Untracked = 2,
    Normal = 3,
    Added = 4,
    Modified = 5,
    Conflict = 6,
}

impl Status {
    pub fn from_byte(b: u8) -> Status {
        match b {
            1 => Status::Ignored,
            2 => Status::Untracked,
            3 => Status::Normal,
            4 => Status::Added,
            5 => Status::Modified,
            6 => Status::Conflict,
            _ => Status::Unknown,
        }
    }
}

// 사용자마다 따로 - 다른 계정 프로세스와 섞이지 않게
pub fn pipe_name() -> String {
    format!(r"\\.\pipe\ieummae-cache-{}", std::env::var("USERNAME").unwrap_or_default())
}

// 저장소 기준 상대 경로 키 - 소문자, / 구분 (Windows 파일 시스템은 대소문자 무시)
pub fn key(rel: &str) -> String {
    rel.replace('\\', "/").trim_end_matches('/').to_lowercase()
}

// 저장소 하나의 상태 표 - 파일과 폴더
#[derive(Debug, Default)]
pub struct RepoStatus {
    pub files: HashMap<String, Status>,
    pub dirs: HashMap<String, Status>,
    // 무시된 폴더 (status --ignored=matching 은 폴더를 통째로 "! 폴더/" 로 줌)
    pub ignored_dirs: Vec<String>,
}

impl RepoStatus {
    // ls-files -z (추적 중인 파일) + status --porcelain=v2 -z -uall --ignored=matching
    pub fn build(ls_files: &str, status: &str) -> RepoStatus {
        let mut s = RepoStatus::default();
        for f in ls_files.split('\0').filter(|f| !f.is_empty()) {
            let k = key(f);
            s.files.insert(k.clone(), Status::Normal);
            s.mark_dirs(&k, Status::Normal);
        }
        // 루트 폴더 - 추적 파일이 하나라도 있으면 정상
        if !s.files.is_empty() {
            s.raise_dir("", Status::Normal);
        }
        let t: Vec<&str> = status.split('\0').collect();
        let mut i = 0;
        while i < t.len() {
            let e = t[i];
            i += 1;
            if e.is_empty() {
                continue;
            }
            let (path, st) = match e.as_bytes()[0] {
                b'1' => match e.splitn(9, ' ').collect::<Vec<_>>().as_slice() {
                    [_, xy, .., p] => (p.to_string(), if xy.starts_with('A') { Status::Added } else { Status::Modified }),
                    _ => continue,
                },
                b'2' => {
                    // 이름 바꾸기 - 다음 토큰은 옛 경로
                    i += 1;
                    match e.splitn(10, ' ').last() { Some(p) => (p.to_string(), Status::Modified), None => continue }
                }
                b'u' => match e.splitn(11, ' ').last() { Some(p) => (p.to_string(), Status::Conflict), None => continue },
                b'?' => (e[2..].to_string(), Status::Untracked),
                b'!' => {
                    let p = &e[2..];
                    if p.ends_with('/') { s.ignored_dirs.push(key(p)); } else { s.files.insert(key(p), Status::Ignored); }
                    continue;
                }
                _ => continue,
            };
            let k = key(&path);
            s.files.insert(k.clone(), st);
            if st != Status::Untracked {
                // 폴더는 아래에서 가장 심각한 상태 (추가는 폴더에선 수정으로)
                let up = if st == Status::Added { Status::Modified } else { st };
                s.mark_dirs(&k, up);
                s.raise_dir("", up);
            }
        }
        s
    }

    fn mark_dirs(&mut self, file_key: &str, st: Status) {
        let mut p = file_key;
        while let Some(i) = p.rfind('/') {
            p = &p[..i];
            self.raise_dir(p, st);
        }
    }

    fn raise_dir(&mut self, dir: &str, st: Status) {
        let e = self.dirs.entry(dir.to_string()).or_insert(st);
        if st > *e {
            *e = st;
        }
    }

    // 상대 경로의 상태 - 파일 표, 폴더 표, 무시된 폴더 아래 순으로
    pub fn get(&self, rel: &str, is_dir: bool) -> Status {
        let k = key(rel);
        let found = if is_dir { self.dirs.get(&k).or_else(|| self.files.get(&k)) } else { self.files.get(&k) };
        if let Some(st) = found {
            return *st;
        }
        if self.ignored_dirs.iter().any(|d| k == *d || k.starts_with(&format!("{d}/"))) {
            return Status::Ignored;
        }
        // 추적 안 하는 파일만 있는 폴더 등
        Status::Unknown
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn 파일과_폴더_상태() {
        let ls = "README.md\0src/a.cs\0src/b.cs\0src/deep/c.cs\0docs/x.md\0";
        let st = [
            "# branch.head main",
            "1 .M N... 100644 100644 100644 a b src/a.cs",
            "1 A. N... 000000 100644 100644 0 a src/deep/new.cs",
            "u UU N... 100644 100644 100644 100644 a b c docs/x.md",
            "? 메모.txt",
            "! bin/",
            "! app.log",
            "",
        ]
        .join("\0");
        let s = RepoStatus::build(ls, &st);

        assert_eq!(s.get("README.md", false), Status::Normal);
        assert_eq!(s.get("src/A.cs", false), Status::Modified);
        assert_eq!(s.get("src/b.cs", false), Status::Normal);
        assert_eq!(s.get("src/deep/new.cs", false), Status::Added);
        assert_eq!(s.get("메모.txt", false), Status::Untracked);
        assert_eq!(s.get("app.log", false), Status::Ignored);
        assert_eq!(s.get("bin/Debug/x.dll", false), Status::Ignored);
        assert_eq!(s.get("bin", true), Status::Ignored);
        // 폴더 - 아래 가장 심각한 상태, 추가는 수정으로
        assert_eq!(s.get("src", true), Status::Modified);
        assert_eq!(s.get("src/deep", true), Status::Modified);
        assert_eq!(s.get("docs", true), Status::Conflict);
        assert_eq!(s.get("", true), Status::Conflict);
    }

    #[test]
    fn 깨끗한_저장소와_이름_바꾸기() {
        let s = RepoStatus::build("a/b.txt\0", "2 R. N... 100644 100644 100644 a a R100 a/c.txt\0a/b.txt\0");
        assert_eq!(s.get("a/c.txt", false), Status::Modified);
        assert_eq!(s.get("a", true), Status::Modified);

        let clean = RepoStatus::build("a/b.txt\0", "");
        assert_eq!(clean.get("a", true), Status::Normal);
        assert_eq!(clean.get("", true), Status::Normal);
        assert_eq!(clean.get("없음.txt", false), Status::Unknown);
    }
}
