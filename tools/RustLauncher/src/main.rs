#![windows_subsystem = "windows"]

mod setup;
mod framework_cloud;
mod protected_cache;

use sha2::{Digest, Sha256};
use std::{
    env, fs,
    io::{self, Read, Write},
    os::windows::{ffi::OsStrExt, fs::{MetadataExt, OpenOptionsExt}},
    path::{Path, PathBuf},
    process::{self, Command},
    time::{Instant, SystemTime, UNIX_EPOCH},
};
use windows_sys::Win32::{
    Foundation::{HWND, CloseHandle},
    System::Threading::{WaitForSingleObject, GetExitCodeProcess},
    UI::Shell::{ShellExecuteExW, SHELLEXECUTEINFOW, SEE_MASK_NOCLOSEPROCESS},
    System::Registry::{RegGetValueW, HKEY_LOCAL_MACHINE},
    UI::{Shell::{IsUserAnAdmin, ShellExecuteW}, WindowsAndMessaging::AllowSetForegroundWindow},
};

const MANAGER: &[u8] = include_bytes!(env!("BARS_EMBEDDED_MANAGER"));
const INSTALLER: &str = "NDP48-x86-x64-AllOS-ENU.exe";
const PACKAGE_FOLDER: &str = "_barsmanager3000";
const MICROSOFT_PAGE: &str = "https://dotnet.microsoft.com/ru-ru/download/dotnet-framework/thank-you/net48-offline-installer";
const SHA256: &str = "0a3a390c47e639d0f7fc65b21195fee6b7f65b066f80f70c60fab191d14b7e40";

fn wide(value: impl AsRef<std::ffi::OsStr>) -> Vec<u16> {
    value.as_ref().encode_wide().chain(Some(0)).collect()
}

fn release() -> (u32, u32) {
    let mut value: u32 = 0;
    let mut size: u32 = 4;
    let status = unsafe {
        RegGetValueW(HKEY_LOCAL_MACHINE,
            wide(r"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full").as_ptr(),
            wide("Release").as_ptr(), 0x20010, std::ptr::null_mut(),
            (&mut value as *mut u32).cast(), &mut size)
    };
    (value, status)
}

fn local_package(folder: &Path) -> Option<PathBuf> {
    [folder.join(INSTALLER), folder.join(PACKAGE_FOLDER).join(INSTALLER)]
        .into_iter().find(|file| file.is_file())
}

fn locate(directory: &Path) -> Option<PathBuf> {
    [Some(directory), directory.parent(), Some(Path::new(r"C:\BarsManager3000"))]
        .into_iter().flatten().find_map(|root| {
            let folder = if root.file_name().is_some_and(|name| name.to_string_lossy().eq_ignore_ascii_case(PACKAGE_FOLDER)) {
                root.to_path_buf()
            } else { root.join(PACKAGE_FOLDER) };
            let file = folder.join(INSTALLER);
            file.is_file().then_some(file)
        })
}

fn open(owner: HWND, verb: &str, target: impl AsRef<std::ffi::OsStr>) -> bool {
    unsafe {
        ShellExecuteW(owner, wide(verb).as_ptr(), wide(target).as_ptr(),
            std::ptr::null(), std::ptr::null(), 1) as isize > 32
    }
}

fn install(file: &Path) -> io::Result<u32> {
    let input = fs::OpenOptions::new().read(true).share_mode(1).open(file)?;
    framework_cloud::verify(file, &input)?;
    let path=wide(file);
    let verb=wide("runas");
    unsafe {
        let mut request: SHELLEXECUTEINFOW=std::mem::zeroed();
        request.cbSize=std::mem::size_of_val(&request) as u32;
        request.fMask=SEE_MASK_NOCLOSEPROCESS;
        request.lpVerb=verb.as_ptr();
        request.lpFile=path.as_ptr();
        request.nShow=1;
        if ShellExecuteExW(&mut request)==0||request.hProcess.is_null() {
            return Err(io::Error::last_os_error());
        }
        drop(input);
        let mut code=0;
        let success=WaitForSingleObject(request.hProcess,u32::MAX)==0 &&
            GetExitCodeProcess(request.hProcess,&mut code)!=0;
        let error=io::Error::last_os_error();
        CloseHandle(request.hProcess);
        if success {Ok(code)} else {Err(error)}
    }
}


fn checked_cache(name: &std::ffi::OsStr) -> io::Result<(PathBuf, fs::File, bool)> {
    let digest = format!("{:x}", Sha256::digest(MANAGER));
    let folder = protected_cache::folder(&digest)?;
    let path = folder.join(name);
    let created = match fs::OpenOptions::new().write(true).create_new(true).share_mode(0).open(&path) {
        Ok(mut output) => { output.write_all(MANAGER)?; true }
        Err(error) if error.kind() == io::ErrorKind::AlreadyExists => false,
        Err(error) => return Err(error),
    };
    if fs::symlink_metadata(&path)?.file_attributes() & 0x400 != 0 {
        return Err(io::Error::other("Точка перенаправления в файле кэша."));
    }
    let mut image = fs::OpenOptions::new().read(true).share_mode(1).open(&path)?;
    if image.metadata()?.len() != MANAGER.len() as u64 {
        return Err(io::Error::other("Размер копии менеджера в кэше отличается."));
    }
    let mut buffer = [0; 64 * 1024];
    for expected in MANAGER.chunks(buffer.len()) {
        let current = &mut buffer[..expected.len()];
        image.read_exact(current)?;
        if current != expected { return Err(io::Error::other("Содержимое копии менеджера в кэше отличается.")); }
    }
    Ok((path, image, created))
}

fn run_manager(file: &Path, directory: &Path, trace: &mut StartupTrace) -> io::Result<i32> {
    trace.mark("native.spawn.begin");
    let mut child = Command::new(file)
        .args(env::args_os().skip(1))
        .current_dir(directory)
        .env("BARS_MANAGER_LAUNCH_DIRECTORY", directory)

        .spawn()?;
    trace.mark("native.spawn.end");
    unsafe { AllowSetForegroundWindow(child.id()); }
    trace.save(child.id());
    Ok(child.wait()?.code().unwrap_or(1))
}

fn start_manager(directory: &Path, name: &std::ffi::OsStr, trace: &mut StartupTrace) -> io::Result<i32> {
    if unsafe { IsUserAnAdmin() } != 0 {
        if let Ok((file, _locked_image, created)) = checked_cache(name) {
            trace.mark(if created { "native.cache-created" } else { "native.cache-hit" });
            return run_manager(&file, directory, trace);
        }
        trace.mark("native.cache-unavailable");
    } else {
        trace.mark("native.cache-skipped-standard");
    }
    let stamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos();
    let temporary = env::temp_dir().join(format!("bars-manager-{}-{stamp}", process::id()));
    fs::create_dir(&temporary)?;
    trace.mark("native.temp-ready");
    let file = temporary.join(name);
    let result = (|| {
        fs::write(&file, MANAGER)?;
        trace.mark("native.extracted");
        run_manager(&file, directory, trace)
    })();
    let _ = fs::remove_file(file);
    let _ = fs::remove_dir(temporary);
    result
}

struct StartupTrace {
    start: Instant,
    origin_ms: u128,
    events: Option<Vec<(&'static str, u128)>>,
}

impl StartupTrace {
    fn new() -> Self {
        let start = Instant::now();
        let origin_ms = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_millis();
        let events = env::args_os().any(|arg| arg == "--profile-ui").then(Vec::new);
        Self { start, origin_ms, events }
    }

    fn mark(&mut self, name: &'static str) {
        if let Some(events) = &mut self.events {
            events.push((name, self.start.elapsed().as_millis()));
        }
    }

    fn save(&self, child: u32) {
        let Some(events) = &self.events else { return; };
        let Some(root) = env::var_os("LOCALAPPDATA") else { return; };
        let directory = PathBuf::from(root).join("BARS").join("Diagnostics");
        if fs::create_dir_all(&directory).is_err() { return; }
        let mut report = format!("native_origin_unix_ms={}\n", self.origin_ms);
        for (name, time) in events {
            report.push_str(&format!("{time} | {name}\n"));
        }
        let _ = fs::write(directory.join(format!("native-startup-{child}.txt")), report);
    }
}

fn main() {
    let mut trace = StartupTrace::new();
    trace.mark("native.entry");
    let (version, status) = release();
    trace.mark("native.framework-checked");
    if env::args_os().nth(1).as_deref() == Some(std::ffi::OsStr::new("--check-framework")) {
        process::exit(if status == 0 && version >= 528040 { 0 } else { 2 });
    }
    if status != 0 && status != 2 && status != 3 {
        setup::show(None, &format!("Не удалось проверить .NET Framework. Код Windows: {status}."), false);
        process::exit(1);
    }
    let executable = match env::current_exe() {
        Ok(file) => file,
        Err(error) => {
            setup::show(None, &format!("Не удалось определить расположение программы. {error}"), false);
            process::exit(1);
        }
    };
    let directory = executable.parent().unwrap_or(Path::new("."));
    if version < 528040 {
        setup::show(locate(directory), "Для запуска BARS Manager 3000 нужен .NET Framework 4.8.", true);
        process::exit(2);
    }
    match start_manager(directory, executable.file_name().unwrap_or(std::ffi::OsStr::new("Bars Manager 3000.exe")), &mut trace) {
        Ok(code) => process::exit(code),
        Err(error) => {
            setup::show(None, &format!("Не удалось открыть BARS Manager 3000. {error}"), false);
            process::exit(1);
        }
    }
}