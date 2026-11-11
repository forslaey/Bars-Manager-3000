use crate::{install, local_package, open, wide, MICROSOFT_PAGE};
use std::{cell::{Cell,RefCell}, mem::zeroed, path::PathBuf, ptr::null_mut};
use windows_sys::Win32::{
    Foundation::*,
    Graphics::Gdi::*,
    System::{Com::*, LibraryLoader::{GetModuleHandleW, GetProcAddress, LoadLibraryW}},
    UI::{Controls::DRAWITEMSTRUCT, Input::KeyboardAndMouse::EnableWindow, Shell::*, WindowsAndMessaging::*},
};



struct State {
    selected: RefCell<Option<PathBuf>>,
    status: Cell<HWND>,
    install: Cell<HWND>,
    dark: bool,
    background: HBRUSH,
}

impl State {
    unsafe fn status(&self, text: &str) { SetWindowTextW(self.status.get(), wide(text).as_ptr()); }
}

unsafe fn choose_folder(owner: HWND) -> Option<PathBuf> {
    if CoInitializeEx(null_mut(), COINIT_APARTMENTTHREADED as u32) < 0 { return None; }
    let mut display = [0u16; 260];
    let title = wide("Выбери папку с установщиком .NET Framework 4.8");
    let mut info: BROWSEINFOW = zeroed();
    info.hwndOwner = owner;
    info.pszDisplayName = display.as_mut_ptr();
    info.lpszTitle = title.as_ptr();
    info.ulFlags = 0x41;
    let id = SHBrowseForFolderW(&info);
    let mut path = [0u16; 260];
    let result = if !id.is_null() && SHGetPathFromIDListW(id,path.as_mut_ptr()) != 0 {
        use std::os::windows::ffi::OsStringExt;
        Some(PathBuf::from(std::ffi::OsString::from_wide(&path[..path.iter().position(|&c|c==0).unwrap_or(path.len())])))
    } else { None };
    if !id.is_null() { CoTaskMemFree(id.cast()); }
    CoUninitialize();
    result
}

unsafe extern "system" fn procedure(window: HWND, message: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    if message == WM_NCCREATE {
        let create = &*(l as *const CREATESTRUCTW);
        SetWindowLongPtrW(window,GWLP_USERDATA,create.lpCreateParams as isize);
    }
    let state = GetWindowLongPtrW(window,GWLP_USERDATA) as *mut State;
    if !state.is_null() {
        let state = &*state;
        match message {
            WM_CTLCOLORSTATIC => {
                SetTextColor(w as HDC, if state.dark {0xf4f7f3} else {0x2f3537});
                SetBkMode(w as HDC, TRANSPARENT as i32);
                return state.background as isize;
            }
            WM_DRAWITEM => {
                let item = &*(l as *const DRAWITEMSTRUCT);
                let accent = item.CtlID == 101 && item.itemState & 4 == 0 && item.itemState & 1 == 0;
                let color = if accent {0x4c8200} else if item.itemState & 1 != 0 {
                    if state.dark {0x405b24} else {0xd9e9ce}
                } else if item.itemState & 4 != 0 {
                    if state.dark {0x293024} else {0xe1ebe4}
                } else if state.dark {0x1e241b} else {0xecf2e9};
                let brush = CreateSolidBrush(color);
                FillRect(item.hDC,&item.rcItem,brush);
                DeleteObject(brush);
                SetTextColor(item.hDC, if accent {0xffffff} else if state.dark {0xf4f7f3} else {0x2f3537});
                SetBkMode(item.hDC,TRANSPARENT as i32);
                let mut label = [0u16;128];
                GetWindowTextW(item.hwndItem,label.as_mut_ptr(),128);
                let mut bounds = item.rcItem;
                DrawTextW(item.hDC,label.as_ptr(),-1,&mut bounds,DT_CENTER|DT_VCENTER|DT_SINGLELINE);
                if item.itemState & 0x10 != 0 { DrawFocusRect(item.hDC,&bounds); }
                return 1;
            }
            WM_COMMAND => {
                match w & 0xffff {
                    101 => if let Some(path)=state.selected.borrow().clone() {
                        match install(window,&path) {
                            Ok(())=>state.status("Установщик запущен. После завершения снова открой BARS Manager 3000."),
                            Err(error)=>state.status(&format!("Не удалось запустить установщик. {error}")),
                        }
                    },
                    102 => if let Some(folder)=choose_folder(window) {
                        state.selected.replace(local_package(&folder));
                        if let Some(path)=state.selected.borrow().clone() {state.status(&path.to_string_lossy());}
                        else {state.status("В выбранной папке установщик не найден.");}
                        let available=state.selected.borrow().is_some(); EnableWindow(state.install.get(),available as i32);
                    },
                    103 => if !open(window,"open",MICROSOFT_PAGE) {state.status("Не удалось открыть страницу Microsoft.");},
                    104 => {DestroyWindow(window);},
                    _ => {}
                }
                return 0;
            }
            WM_DESTROY => {PostQuitMessage(0);return 0;}
            _ => {}
        }
    }
    DefWindowProcW(window,message,w,l)
}

fn dark_theme() -> bool {
    let root=std::env::var_os("LOCALAPPDATA").map(PathBuf::from).unwrap_or_default().join("BARS");
    ["Bars Service Manager 3000","ServiceManager3000"].into_iter()
        .find_map(|name| std::fs::read_to_string(root.join(name).join("theme.txt")).ok())
        .is_some_and(|text| text.trim()=="dark")
}

pub fn show(selected: Option<PathBuf>, text: &str, can_install: bool) {
    unsafe {
        SetProcessDPIAware();
        let dc=GetDC(null_mut()); let dpi=if dc.is_null(){96}else{let result=GetDeviceCaps(dc,LOGPIXELSX as i32).max(96);ReleaseDC(null_mut(),dc);result};
        let px = |value: i32| value*dpi/96;
        let dark = dark_theme();
        let background = CreateSolidBrush(if dark {0x141712} else {0xf8f9f7});
        let instance = GetModuleHandleW(std::ptr::null());
        let name = wide("BARS.Rust.Framework.Setup");
        let mut icon=null_mut(); if let Ok(exe)=std::env::current_exe(){ExtractIconExW(wide(exe).as_ptr(),0,&mut icon,null_mut(),1); }
        let mut class: WNDCLASSW = zeroed();
        class.lpfnWndProc = Some(procedure);
        class.hInstance = instance;
        class.hIcon = icon;
        class.hCursor = LoadCursorW(null_mut(),IDC_ARROW);
        class.hbrBackground = background;
        class.lpszClassName = name.as_ptr();
        if RegisterClassW(&class)==0 {DeleteObject(background);return;}
        let mut state=Box::new(State{selected:RefCell::new(selected),status:Cell::new(null_mut()),install:Cell::new(null_mut()),dark,background});
        let (width,height)=(px(652),px(260));
        let window=CreateWindowExW(0,name.as_ptr(),wide("BARS Manager 3000").as_ptr(),
            WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,
            ((GetSystemMetrics(SM_CXSCREEN)-width)/2).max(0),((GetSystemMetrics(SM_CYSCREEN)-height)/2).max(0),
            width,height,null_mut(),null_mut(),instance,(&mut *state as *mut State).cast());
        if window.is_null() {DeleteObject(background);return;}
        if dark {
            let value=1;
                        let module=LoadLibraryW(wide("dwmapi.dll").as_ptr());
            if !module.is_null() {
                if let Some(raw)=GetProcAddress(module,b"DwmSetWindowAttribute\0".as_ptr()) {
                    let apply: unsafe extern "system" fn(HWND,u32,*const i32,u32)->i32=std::mem::transmute(raw);
                    if apply(window,20,&value,4)<0 {apply(window,19,&value,4);}
                }
            }
        }
        let font=CreateFontW(-px(17),0,0,0,400,0,0,0,1,0,0,5,0,wide("Segoe UI").as_ptr());
        let child=|kind:&str,label:&str,style:u32,id:usize,x:i32,y:i32,w:i32,h:i32| {
            let control=CreateWindowExW(0,wide(kind).as_ptr(),wide(label).as_ptr(),WS_CHILD|WS_VISIBLE|style,
                px(x),px(y),px(w),px(h),window,id as HMENU,instance,std::ptr::null());
            SendMessageW(control,WM_SETFONT,font as usize,1);
            control
        };
        child("STATIC","ПОДГОТОВКА BARS MANAGER 3000",0,0,20,14,590,28);
        child("STATIC",text,0,0,20,48,590,45);
        state.status.set(child("STATIC","",0,0,20,98,590,54));
        if can_install {
            if let Some(path)=state.selected.borrow().clone() {state.status(&path.to_string_lossy());}
            else {state.status("Локальный установщик не найден. Выбери папку или открой сайт Microsoft.");}
            state.install.set(child("BUTTON","Установить",WS_TABSTOP|BS_OWNERDRAW as u32,101,20,164,144,40));
            let available=state.selected.borrow().is_some(); EnableWindow(state.install.get(),available as i32);
            child("BUTTON","Выбрать папку…",WS_TABSTOP|BS_OWNERDRAW as u32,102,174,164,144,40);
            child("BUTTON","Скачать…",WS_TABSTOP|BS_OWNERDRAW as u32,103,328,164,144,40);
        }
        child("BUTTON","Закрыть",WS_TABSTOP|BS_OWNERDRAW as u32,104,482,164,144,40);
        ShowWindow(window,SW_SHOWNORMAL);
        UpdateWindow(window);
        let mut message:MSG=zeroed();
        while GetMessageW(&mut message,null_mut(),0,0)>0 {
            if IsDialogMessageW(window,&message)==0 {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        DeleteObject(font);
        if !icon.is_null(){DestroyIcon(icon);}
        DeleteObject(background);
    }
}