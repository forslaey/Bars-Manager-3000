use crate::{wide, INSTALLER, SHA256};
use sha2::{Digest, Sha256};
use std::{fs, io::{self, Read, Write}, path::{Path, PathBuf}, ptr::{null, null_mut}, time::{SystemTime, UNIX_EPOCH}};
use windows_sys::{core::GUID, Win32::{Networking::WinHttp::*, Security::WinTrust::*}};

const SIZE: u64 = 121_346_568;
const YANDEX: &str = "https://cloud-api.yandex.net/v1/disk/public/resources/download?public_key=https%3A%2F%2Fdisk.yandex.ru%2Fd%2FI0sX3DFWoXUQWQ&path=%2F_barsmanager3000%2FNDP48-x86-x64-AllOS-ENU.exe";
// Public file in the verified mirror's _barsmanager3000 directory.
const GOOGLE: &str = "https://drive.usercontent.google.com/download?id=1eq6O23As41IA7WY5GeQFcp847cmpAo9p&export=download&confirm=t";

struct Handle(*mut core::ffi::c_void);
impl Handle {
    fn new(raw: *mut core::ffi::c_void) -> io::Result<Self> {
        if raw.is_null() { Err(io::Error::last_os_error()) } else { Ok(Self(raw)) }
    }
}
impl Drop for Handle { fn drop(&mut self) { unsafe { WinHttpCloseHandle(self.0); } } }

fn get(url: &str, limit: u64, mut output: impl Write) -> io::Result<u64> {
    let (host, resource) = url.strip_prefix("https://").and_then(|url| url.split_once('/'))
        .filter(|(host, _)| !host.is_empty() && host.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'.' || b == b'-'))
        .ok_or_else(|| io::Error::other("Источник вернул недопустимый HTTPS-адрес."))?;
    unsafe {
        let session = Handle::new(WinHttpOpen(wide("BARS Manager 3000").as_ptr(), WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, null(), null(), 0))?;
        WinHttpSetTimeouts(session.0, 15_000, 20_000, 20_000, 30_000);
        let connection = Handle::new(WinHttpConnect(session.0, wide(host).as_ptr(), 443, 0))?;
        let request = Handle::new(WinHttpOpenRequest(connection.0, wide("GET").as_ptr(), wide(format!("/{resource}")).as_ptr(), null(), null(), null(), WINHTTP_FLAG_SECURE))?;
        if WinHttpSendRequest(request.0, null(), 0, null(), 0, 0, 0) == 0 || WinHttpReceiveResponse(request.0, null_mut()) == 0 {
            return Err(io::Error::last_os_error());
        }
        let mut status = 0u32;
        let mut length = 4;
        if WinHttpQueryHeaders(request.0, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, null(), (&mut status as *mut u32).cast(), &mut length, null_mut()) == 0 {
            return Err(io::Error::last_os_error());
        }
        if status != 200 { return Err(io::Error::other(format!("Источник ответил HTTP {status}."))); }
        let mut buffer = [0u8; 64 * 1024];
        let mut total = 0;
        loop {
            let mut count = 0;
            if WinHttpReadData(request.0, buffer.as_mut_ptr().cast(), buffer.len() as u32, &mut count) == 0 { return Err(io::Error::last_os_error()); }
            if count == 0 { return Ok(total); }
            total += u64::from(count);
            if total > limit { return Err(io::Error::other("Источник вернул файл неожиданного размера.")); }
            output.write_all(&buffer[..count as usize])?;
        }
    }
}

pub fn verify(path: &Path, mut input: impl Read) -> io::Result<()> {
    let mut hash = Sha256::new();
    io::copy(&mut input, &mut hash)?;
    if format!("{:x}", hash.finalize()) != SHA256 {
        return Err(io::Error::other("Файл отличается от проверенного пакета Microsoft."));
    }
    let name = wide(path);
    unsafe {
        let mut file: WINTRUST_FILE_INFO = std::mem::zeroed();
        file.cbStruct = std::mem::size_of_val(&file) as u32;
        file.pcwszFilePath = name.as_ptr();
        let mut data: WINTRUST_DATA = std::mem::zeroed();
        data.cbStruct = std::mem::size_of_val(&data) as u32;
        data.dwUIChoice = WTD_UI_NONE;
        data.dwUnionChoice = WTD_CHOICE_FILE;
        data.Anonymous.pFile = &mut file;
        data.dwStateAction = WTD_STATEACTION_VERIFY;
        let mut policy = GUID::from_u128(0x00aac56b_cd44_11d0_8cc2_00c04fc295ee);
        let result = WinVerifyTrust(null_mut(), &mut policy, (&mut data as *mut WINTRUST_DATA).cast());
        data.dwStateAction = WTD_STATEACTION_CLOSE;
        WinVerifyTrust(null_mut(), &mut policy, (&mut data as *mut WINTRUST_DATA).cast());
        if result != 0 { return Err(io::Error::other(format!("Подпись установщика не подтверждена (0x{:08X}).", result as u32))); }
    }
    Ok(())
}

pub fn download(yandex: bool) -> io::Result<PathBuf> {
    let mut metadata = Vec::new();
    let link = if yandex {
        get(YANDEX, 64 * 1024, &mut metadata)?;
        let text = std::str::from_utf8(&metadata).map_err(io::Error::other)?;
        text.split_once("\"href\"").and_then(|(_, value)| value.trim_start().strip_prefix(':'))
            .and_then(|value| value.trim_start().strip_prefix('"')).and_then(|value| value.split_once('"'))
            .map(|(url, _)| url).ok_or_else(|| io::Error::other("Яндекс Диск не предоставил ссылку на установщик."))?
    } else { GOOGLE };
    let link = link.replace("\\/", "/");
    let stamp = SystemTime::now().duration_since(UNIX_EPOCH).unwrap_or_default().as_nanos();
    let folder = std::env::temp_dir().join(format!("bars-framework-{}-{stamp}", std::process::id()));
    fs::create_dir(&folder)?;
    let file = folder.join(INSTALLER);
    let result = (|| {
        let size = get(&link, SIZE, fs::OpenOptions::new().write(true).create_new(true).open(&file)?)?;
        if size != SIZE { return Err(io::Error::other("Установщик скачан не полностью.")); }
        verify(&file, fs::File::open(&file)?)?;
        Ok(file.clone())
    })();
    if result.is_err() { remove(&file); }
    result
}

pub fn remove(file: &Path) {
    let _ = fs::remove_file(file);
    if let Some(folder) = file.parent() { let _ = fs::remove_dir(folder); }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_wrong_payload() {
        let error = verify(Path::new(INSTALLER), &b"wrong installer"[..]).unwrap_err();
        assert!(error.to_string().contains("отличается"));
    }

    #[test]
    #[ignore = "downloads the Framework installer without executing it"]
    fn verifies_live_mirrors() {
        for yandex in [true, false] {
            let file = download(yandex).expect(if yandex { "Yandex Disk" } else { "Google Drive" });
            assert_eq!(fs::metadata(&file).unwrap().len(), SIZE);
            remove(&file);
        }
    }
}
