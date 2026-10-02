// 우클릭한 위치 판별 - 저장소 안·밖, 파일·폴더, 진행 중인 병합 등
// git 프로세스 없이 파일 시스템만 봄 (메뉴가 뜨는 순간 불리므로 빨라야 함)
use std::path::{Path, PathBuf};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Location {
    pub path: PathBuf,
    pub is_file: bool,
    pub repo: Option<PathBuf>,
    pub operation: bool,
}

impl Location {
    pub fn of(path: &Path) -> Location {
        let is_file = path.is_file();
        let start = if is_file { path.parent().unwrap_or(path) } else { path };
        let mut repo = None;
        let mut operation = false;
        let mut d = Some(start);
        while let Some(dir) = d {
            let git = dir.join(".git");
            if git.is_dir() || git.is_file() {
                operation = in_progress(&git_dir(&git));
                repo = Some(dir.to_path_buf());
                break;
            }
            d = dir.parent();
        }
        Location { path: path.to_path_buf(), is_file, repo, operation }
    }
}

// worktree 의 .git 은 파일 - "gitdir: 경로" 를 따라감
fn git_dir(git: &Path) -> PathBuf {
    if git.is_file() {
        if let Ok(s) = std::fs::read_to_string(git) {
            if let Some(rest) = s.trim().strip_prefix("gitdir:") {
                let p = PathBuf::from(rest.trim());
                return if p.is_absolute() { p } else { git.parent().unwrap_or(git).join(p) };
            }
        }
    }
    git.to_path_buf()
}

// 병합·리베이스·체리픽·되돌리기 중 - git 이 남기는 표시 파일
fn in_progress(dir: &Path) -> bool {
    ["MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD"].iter().any(|f| dir.join(f).is_file())
        || ["rebase-merge", "rebase-apply"].iter().any(|f| dir.join(f).is_dir())
}

// 메뉴 항목 - id 는 ieummae.exe 명령, None 은 구분선
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Item {
    pub id: Option<&'static str>,
    pub title: &'static str,
}

const fn cmd(id: &'static str, title: &'static str) -> Item { Item { id: Some(id), title } }
const SEP: Item = Item { id: None, title: "" };

// 위치별 항목 - 화면 git 용어는 영어 그대로
pub fn items(loc: &Location) -> Vec<Item> {
    let mut v = Vec::new();
    if loc.repo.is_none() {
        v.extend([cmd("clone", "Clone"), cmd("init", "Init"), SEP, cmd("settings", "설정")]);
        return v;
    }
    if loc.operation {
        v.extend([cmd("conflicts", "Resolve"), SEP]);
    }
    v.extend([cmd("log", "Log"), cmd("commit", "Commit")]);
    if loc.is_file {
        v.extend([cmd("diff", "Diff"), cmd("blame", "Blame")]);
    }
    v.extend([
        SEP,
        cmd("fetch", "Fetch"), cmd("pull", "Pull"), cmd("push", "Push"),
        SEP,
        cmd("switch", "Switch"), cmd("branch", "New Branch"), cmd("merge", "Merge"), cmd("rebase", "Rebase"),
        SEP,
        cmd("stash", "Stash"), cmd("stash-list", "Stash List"),
        SEP,
        cmd("settings", "설정"),
    ]);
    v
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp(name: &str) -> PathBuf {
        let p = std::env::temp_dir().join(format!("ieummae-menu-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&p);
        std::fs::create_dir_all(&p).unwrap();
        p
    }

    fn ids(loc: &Location) -> Vec<&'static str> { items(loc).iter().filter_map(|i| i.id).collect() }

    #[test]
    fn 저장소_밖() {
        let d = temp("out");
        let loc = Location::of(&d);
        assert_eq!(loc.repo, None);
        assert_eq!(ids(&loc), ["clone", "init", "settings"]);
    }

    #[test]
    fn 저장소_안_파일과_폴더() {
        let d = temp("in");
        std::fs::create_dir_all(d.join(".git")).unwrap();
        std::fs::create_dir_all(d.join("src")).unwrap();
        std::fs::write(d.join("src").join("a.txt"), "x").unwrap();

        let folder = Location::of(&d.join("src"));
        assert_eq!(folder.repo.as_deref(), Some(d.as_path()));
        assert!(!ids(&folder).contains(&"blame"));

        let file = Location::of(&d.join("src").join("a.txt"));
        assert!(file.is_file);
        assert!(ids(&file).starts_with(&["log", "commit", "diff", "blame"]));
    }

    #[test]
    fn 병합_중이면_resolve_먼저() {
        let d = temp("merge");
        std::fs::create_dir_all(d.join(".git")).unwrap();
        std::fs::write(d.join(".git").join("MERGE_HEAD"), "abc").unwrap();
        assert_eq!(ids(&Location::of(&d))[0], "conflicts");
    }

    #[test]
    fn worktree_의_git_파일() {
        let d = temp("wt");
        let real = d.join("real-git");
        std::fs::create_dir_all(real.join("rebase-merge")).unwrap();
        let wt = d.join("wt");
        std::fs::create_dir_all(&wt).unwrap();
        std::fs::write(wt.join(".git"), format!("gitdir: {}\n", real.display())).unwrap();
        let loc = Location::of(&wt);
        assert_eq!(loc.repo.as_deref(), Some(wt.as_path()));
        assert!(loc.operation);
    }
}
