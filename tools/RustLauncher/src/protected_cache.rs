use crate::wide;
use std::{ffi::OsString, fs, io, os::windows::{ffi::OsStringExt, fs::MetadataExt}, path::{Path, PathBuf}, ptr::null_mut};
use windows_sys::Win32::{
    Foundation::{LocalFree, GetLastError, ERROR_ALREADY_EXISTS},
    Security::{Authorization::{ConvertSecurityDescriptorToStringSecurityDescriptorW, ConvertStringSecurityDescriptorToSecurityDescriptorW},
        GetFileSecurityW, DACL_SECURITY_INFORMATION, OWNER_SECURITY_INFORMATION, SECURITY_ATTRIBUTES},
    Storage::FileSystem::CreateDirectoryW,
    System::Com::CoTaskMemFree,
    UI::Shell::{FOLDERID_ProgramFiles, SHGetKnownFolderPath},
};

const ACL: &str = "O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

unsafe fn utf16_z(pointer: *const u16) -> OsString {
    let mut length = 0;
    while *pointer.add(length) != 0 { length += 1; }
    OsString::from_wide(std::slice::from_raw_parts(pointer, length))
}

fn program_files() -> io::Result<PathBuf> {
    unsafe {
        let mut name = null_mut();
        let result = SHGetKnownFolderPath(&FOLDERID_ProgramFiles, 0, null_mut(), &mut name);
        if result < 0 || name.is_null() { return Err(io::Error::other("Не найдена папка Program Files.")); }
        let path = PathBuf::from(utf16_z(name));
        CoTaskMemFree(name.cast());
        Ok(path)
    }
}

fn protected_directory(path: &Path) -> io::Result<()> {
    unsafe {
        let name = wide(path);
        let mut descriptor = null_mut();
        if ConvertStringSecurityDescriptorToSecurityDescriptorW(wide(ACL).as_ptr(), 1, &mut descriptor, null_mut()) == 0 {
            return Err(io::Error::last_os_error());
        }
        let attributes = SECURITY_ATTRIBUTES {
            nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
            lpSecurityDescriptor: descriptor,
            bInheritHandle: 0,
        };
        let created = CreateDirectoryW(name.as_ptr(), &attributes);
        let result = GetLastError();
        LocalFree(descriptor.cast());
        if created == 0 && result != ERROR_ALREADY_EXISTS { return Err(io::Error::from_raw_os_error(result as i32)); }
        let metadata = fs::symlink_metadata(path)?;
        if !metadata.is_dir() || metadata.file_attributes() & 0x400 != 0 {
            return Err(io::Error::other("Недопустимая папка кэша."));
        }
        let info = OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION;
        let mut size = 0;
        GetFileSecurityW(name.as_ptr(), info, null_mut(), 0, &mut size);
        if size == 0 || size > 65536 { return Err(io::Error::other("Не удалось проверить права папки кэша.")); }
        let mut buffer = vec![0usize; (size as usize).div_ceil(std::mem::size_of::<usize>())];
        let descriptor = buffer.as_mut_ptr().cast();
        if GetFileSecurityW(name.as_ptr(), info, descriptor, size, &mut size) == 0 { return Err(io::Error::last_os_error()); }
        let mut text = null_mut();
        if ConvertSecurityDescriptorToStringSecurityDescriptorW(descriptor, 1, info, &mut text, null_mut()) == 0 {
            return Err(io::Error::last_os_error());
        }
        let access = utf16_z(text);
        LocalFree(text.cast());
        if access != ACL { return Err(io::Error::other("Права папки кэша не соответствуют ожидаемым.")); }
        Ok(())
    }
}

pub fn folder(digest: &str) -> io::Result<PathBuf> {
    let root = program_files()?.join("BARS Manager 3000 Startup Cache");
    protected_directory(&root)?;
    let version = root.join(digest);
    protected_directory(&version)?;
    Ok(version)
}