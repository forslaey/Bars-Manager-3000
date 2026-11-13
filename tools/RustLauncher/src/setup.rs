use crate::{framework_cloud, install, local_package, open, wide, MICROSOFT_PAGE};
use std::{cell::{Cell,RefCell}, mem::zeroed, path::PathBuf, ptr::null_mut};
use windows_sys::Win32::{
    Foundation::*,
    Graphics::Gdi::*,
    System::LibraryLoader::{GetModuleHandleW, GetProcAddress, LoadLibraryW},
    UI::{Controls::{CheckRadioButton, IsDlgButtonChecked, DRAWITEMSTRUCT}, Input::KeyboardAndMouse::EnableWindow, Shell::*, WindowsAndMessaging::*},
};



struct State {
    selected: RefCell<Option<PathBuf>>,
    status: Cell<HWND>,
    install: Cell<HWND>,
    download_button: Cell<HWND>,
    download: RefCell<Option<std::sync::mpsc::Receiver<std::io::Result<PathBuf>>>>,
    temporary: RefCell<Option<PathBuf>>,
    installing: RefCell<Option<std::sync::mpsc::Receiver<std::io::Result<u32>>>>,
    dark: bool,
    background: HBRUSH,
}

impl State {
    unsafe fn status(&self, text: &str) { SetWindowTextW(self.status.get(), wide(text).as_ptr()); }
}

struct FolderPicker {
    path: PathBuf,
    children: Vec<PathBuf>,
    selected: Option<PathBuf>,
    edit: HWND,
    list: HWND,
    dark: bool,
    brush: HBRUSH,
}

impl FolderPicker {
    unsafe fn show_paths(&mut self, path: Option<PathBuf>) {
        let mut children: Vec<PathBuf> = match &path {
            Some(folder) => match std::fs::read_dir(folder) {
                Ok(entries) => entries.filter_map(Result::ok).map(|entry| entry.path()).filter(|p|p.is_dir()).collect(),
                Err(_) => return,
            },
            None => ('A'..='Z').map(|letter|PathBuf::from(format!("{letter}:\\"))).filter(|p|p.is_dir()).collect(),
        };
        children.sort_by_key(|p|p.to_string_lossy().to_lowercase());
        self.path = path.unwrap_or_default();
        SetWindowTextW(self.edit, wide(&self.path).as_ptr());
        SendMessageW(self.list, LB_RESETCONTENT, 0, 0);
        for item in &children {
            let label = item.file_name().map(|name|name.to_string_lossy()).unwrap_or_else(||item.to_string_lossy());
            SendMessageW(self.list, LB_ADDSTRING, 0, wide(label.as_ref()).as_ptr() as isize);
        }
        self.children = children;
    }

    unsafe fn typed_path(&self) -> PathBuf {
        use std::os::windows::ffi::OsStringExt;
        let mut text = vec![0u16; GetWindowTextLengthW(self.edit).max(0) as usize + 1];
        let count = GetWindowTextW(self.edit, text.as_mut_ptr(), text.len() as i32).max(0) as usize;
        PathBuf::from(std::ffi::OsString::from_wide(&text[..count]))
    }

    unsafe fn highlighted(&self) -> Option<PathBuf> {
        let index = SendMessageW(self.list, LB_GETCURSEL, 0, 0);
        self.children.get(index as usize).cloned()
    }
}

unsafe extern "system" fn folder_proc(window: HWND, message: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    if message == WM_NCCREATE {
        SetWindowLongPtrW(window, GWLP_USERDATA, (*(l as *const CREATESTRUCTW)).lpCreateParams as isize);
    }
    let state = GetWindowLongPtrW(window,GWLP_USERDATA) as *mut FolderPicker;
    if state.is_null() { return DefWindowProcW(window,message,w,l); }
    let state = &mut *state;
    match message {
        WM_CTLCOLOREDIT | WM_CTLCOLORLISTBOX => {
            SetTextColor(w as HDC, if state.dark {0xf4f7f3} else {0x2f3537});
            SetBkColor(w as HDC, if state.dark {0x241f19} else {0xf8f9f7});
            return state.brush as isize;
        }
        WM_DRAWITEM => {
            let item = &*(l as *const DRAWITEMSTRUCT);
            let fill = CreateSolidBrush(if item.CtlID==204 {0x4c8200} else if state.dark {0x302a21} else {0xe5eee8});
            FillRect(item.hDC,&item.rcItem,fill);
            DeleteObject(fill);
            SetBkMode(item.hDC,TRANSPARENT as i32);
            SetTextColor(item.hDC,if item.CtlID==204||state.dark {0xffffff} else {0x2f3537});
            let mut text=[0u16;80];
            GetWindowTextW(item.hwndItem,text.as_mut_ptr(),80);
            let mut rect=item.rcItem;
            DrawTextW(item.hDC,text.as_ptr(),-1,&mut rect,DT_CENTER|DT_VCENTER|DT_SINGLELINE);
            if item.itemState&0x10!=0 {DrawFocusRect(item.hDC,&rect);}
            return 1;
        }
        WM_COMMAND => {
            match (w&0xffff, (w>>16)&0xffff) {
                (201,_) => state.show_paths(state.path.parent().map(|p|p.to_path_buf())),
                (202,_) => { let path=state.typed_path();if path.is_absolute(){state.show_paths(Some(path));} }
                (203,_) => {DestroyWindow(window);}
                (204,_) => {
                    let typed=state.typed_path();
                    let selected=if typed==state.path {state.highlighted().unwrap_or(typed)} else {typed};
                    if selected.is_absolute()&&selected.is_dir(){state.selected=Some(selected);DestroyWindow(window);}
                }
                (205,2) => {if let Some(path)=state.highlighted(){state.show_paths(Some(path));}}
                _=>{}
            }
            return 0;
        }
        WM_CLOSE => {DestroyWindow(window);return 0;}
        _=>{}
    }
    DefWindowProcW(window,message,w,l)
}

unsafe fn title_theme(window: HWND, dark: bool) {
    let module=LoadLibraryW(wide("dwmapi.dll").as_ptr());
    if module.is_null(){return;}
    if let Some(raw)=GetProcAddress(module,b"DwmSetWindowAttribute\0".as_ptr()) {
        let apply:unsafe extern "system" fn(HWND,u32,*const i32,u32)->i32=std::mem::transmute(raw);
        let mode=dark as i32;
        if apply(window,20,&mode,4)<0{apply(window,19,&mode,4);}
        let caption=if dark {0x1e241b}else{0xf8f9f7};
        let text=if dark {0xf4f7f3}else{0x2f3537};
        apply(window,35,&caption,4);apply(window,36,&text,4);
    }
}
unsafe fn choose_folder(owner: HWND, dark: bool) -> Option<PathBuf> {
    let instance=GetModuleHandleW(null_mut());
    let class_name=wide("BARS.Rust.FolderPicker");
    let brush=CreateSolidBrush(if dark {0x241f19} else {0xf8f9f7});
    let mut class:WNDCLASSW=zeroed();
    class.lpfnWndProc=Some(folder_proc);class.hInstance=instance;class.lpszClassName=class_name.as_ptr();
    class.hCursor=LoadCursorW(null_mut(),IDC_ARROW);class.hbrBackground=brush;
    class.hIcon=SendMessageW(owner,WM_GETICON,1,0) as HICON;
    if class.hIcon.is_null(){class.hIcon=GetClassLongPtrW(owner,GCLP_HICON) as HICON;}
    if RegisterClassW(&class)==0 {DeleteObject(brush);return None;}
    let mut state=FolderPicker{path:PathBuf::new(),children:Vec::new(),selected:None,edit:null_mut(),list:null_mut(),dark,brush};
    let mut bounds:RECT=zeroed();GetWindowRect(owner,&mut bounds);
    let dc=GetDC(owner);let dpi=if dc.is_null(){96}else{let result=GetDeviceCaps(dc,LOGPIXELSX as i32).max(96);ReleaseDC(owner,dc);result};
    let px=|value:i32|value*dpi/96;
    let window=CreateWindowExW(0,class_name.as_ptr(),wide("Выбери папку с установщиком").as_ptr(),
        WS_CAPTION|WS_SYSMENU|WS_THICKFRAME,(bounds.left+(bounds.right-bounds.left-px(560))/2).max(0),
        (bounds.top+(bounds.bottom-bounds.top-px(410))/2).max(0),px(560),px(410),owner,null_mut(),instance,(&mut state as *mut FolderPicker).cast());
    if window.is_null(){UnregisterClassW(class_name.as_ptr(),instance);DeleteObject(brush);return None;}
    title_theme(window,dark);
    let font=CreateFontW(-px(16),0,0,0,400,0,0,0,1,0,0,5,0,wide("Segoe UI").as_ptr());
    let child=|kind:&str,label:&str,style:u32,id:usize,x:i32,y:i32,w:i32,h:i32|{
        let control=CreateWindowExW(0,wide(kind).as_ptr(),wide(label).as_ptr(),WS_CHILD|WS_VISIBLE|WS_TABSTOP|style,
            px(x),px(y),px(w),px(h),window,id as HMENU,instance,null_mut());
        SendMessageW(control,WM_SETFONT,font as usize,1);
        control
    };
    state.edit=child("EDIT","",WS_BORDER|ES_AUTOHSCROLL as u32,206,18,18,340,32);
    child("BUTTON","Выше",BS_OWNERDRAW as u32,201,368,18,76,32);
    child("BUTTON","Перейти",BS_OWNERDRAW as u32,202,454,18,76,32);
    state.list=child("LISTBOX","",WS_BORDER|WS_VSCROLL|LBS_NOTIFY as u32,205,18,62,512,232);
    child("BUTTON","Отмена",BS_OWNERDRAW as u32,203,254,312,132,38);
    child("BUTTON","Выбрать папку",BS_OWNERDRAW as u32,204,398,312,132,38);
    let default=PathBuf::from(r"C:\BarsManager3000");
    state.show_paths(if default.is_dir(){Some(default)}else{None});
    EnableWindow(owner,0);ShowWindow(window,SW_SHOWNORMAL);
    let mut message:MSG=zeroed();
    while IsWindow(window)!=0 {
        let result=GetMessageW(&mut message,null_mut(),0,0);
        if result<=0 {if result==0{PostQuitMessage(message.wParam as i32);}break;}
        if IsDialogMessageW(window,&message)==0{TranslateMessage(&message);DispatchMessageW(&message);}
    }
    if IsWindow(window)!=0{DestroyWindow(window);}
    EnableWindow(owner,1);SetForegroundWindow(owner);
    DeleteObject(font);UnregisterClassW(class_name.as_ptr(),instance);DeleteObject(brush);
    state.selected
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
                    101 if state.download.borrow().is_none() && state.installing.borrow().is_none() => if let Some(path)=state.selected.borrow().clone() {
                        if SetTimer(window,2,150,None)==0 {state.status("Не удалось подготовить установку.");return 0;}
                        let (tx,rx)=std::sync::mpsc::channel();
                        state.installing.replace(Some(rx));
                        EnableWindow(state.install.get(),0);
                        EnableWindow(state.download_button.get(),0);
                        state.status("Ожидаем завершения установщика .NET Framework 4.8…");
                        std::thread::spawn(move || {let _=tx.send(install(&path));});
                    },
                    102 if state.download.borrow().is_none() && state.installing.borrow().is_none() => if let Some(folder)=choose_folder(window,state.dark) {
                        if let Some(path)=state.temporary.borrow_mut().take(){framework_cloud::remove(&path);}
                        state.selected.replace(local_package(&folder));
                        if let Some(path)=state.selected.borrow().clone() {state.status(&path.to_string_lossy());}
                        else {state.status("В выбранной папке установщик не найден.");}
                        let available=state.selected.borrow().is_some(); EnableWindow(state.install.get(),available as i32);
                    },
                    103 if state.download.borrow().is_none() && state.installing.borrow().is_none() => {
                        if IsDlgButtonChecked(window,107)==1 {
                            if !open(window,"open",MICROSOFT_PAGE) {state.status("Не удалось открыть страницу Microsoft.");}
                        } else {
                            let yandex=IsDlgButtonChecked(window,105)==1;
                            if SetTimer(window,1,150,None)==0 {state.status("Не удалось запустить загрузку.");return 0;}
                            let (tx,rx)=std::sync::mpsc::channel();
                            state.download.replace(Some(rx));
                            EnableWindow(state.download_button.get(),0);
                            EnableWindow(state.install.get(),0);
                            state.status("Скачиваем и проверяем установщик .NET Framework 4.8…");
                            std::thread::spawn(move || {let _=tx.send(framework_cloud::download(yandex));});
                        }
                    },
                    104 => {SendMessageW(window,WM_CLOSE,0,0);},
                    _ => {}
                }
                return 0;
            }
            WM_TIMER if w==1 => {
                use std::sync::mpsc::TryRecvError;
                let result = match state.download.borrow().as_ref().map(|receiver| receiver.try_recv()) {
                    Some(Ok(result))=>result,
                    Some(Err(TryRecvError::Disconnected))=>Err(std::io::Error::other("Загрузка прервалась.")),
                    _=>return 0,
                };
                KillTimer(window,1);
                state.download.borrow_mut().take();
                EnableWindow(state.download_button.get(),1);
                match result {
                    Ok(path)=>{
                        if let Some(old)=state.temporary.replace(Some(path.clone())) {framework_cloud::remove(&old);}
                        state.selected.replace(Some(path));
                        state.status("Установщик скачан и проверен. Нажми «Установить».");
                    },
                    Err(error)=>state.status(&format!("Не удалось скачать установщик. {error}")),
                }
                EnableWindow(state.install.get(),state.selected.borrow().is_some() as i32);
                return 0;
            }
            WM_TIMER if w==2 => {
                use std::sync::mpsc::TryRecvError;
                let result=match state.installing.borrow().as_ref().map(|receiver| receiver.try_recv()){
                    Some(Ok(result))=>result,
                    Some(Err(TryRecvError::Disconnected))=>Err(std::io::Error::other("Ожидание установки прервано.")),
                    _=>return 0,
                };
                KillTimer(window,2);
                state.installing.borrow_mut().take();
                EnableWindow(state.download_button.get(),1);
                match result {
                    Ok(code)=>{
                        if let Some(path)=state.temporary.borrow_mut().take(){
                            framework_cloud::remove(&path);
                            state.selected.borrow_mut().take();
                        }
                        match code{
                            0=>state.status("Установка завершена. Снова открой BARS Manager 3000."),
                            1641|3010=>state.status("Установка завершена. Требуется перезагрузка Windows."),
                            _=>state.status(&format!("Установщик завершился с кодом {code}.")),
                        }
                    },
                    Err(error)=>state.status(&format!("Не удалось выполнить установку. {error}")),
                }
                EnableWindow(state.install.get(),state.selected.borrow().is_some() as i32);
                return 0;
            }
            WM_CLOSE if state.download.borrow().is_some()||state.installing.borrow().is_some()=>return 0,
            WM_DESTROY => {
                if let Some(path)=state.temporary.borrow_mut().take(){framework_cloud::remove(&path);}
                PostQuitMessage(0);return 0;
            }
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
        let mut state=Box::new(State{selected:RefCell::new(selected),status:Cell::new(null_mut()),install:Cell::new(null_mut()),download_button:Cell::new(null_mut()),download:RefCell::new(None),temporary:RefCell::new(None),installing:RefCell::new(None),dark,background});
        let (width,height)=(px(652),px(if can_install {300} else {260}));
        let window=CreateWindowExW(0,name.as_ptr(),wide("BARS Manager 3000").as_ptr(),
            WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX|WS_THICKFRAME,
            ((GetSystemMetrics(SM_CXSCREEN)-width)/2).max(0),((GetSystemMetrics(SM_CYSCREEN)-height)/2).max(0),
            width,height,null_mut(),null_mut(),instance,(&mut *state as *mut State).cast());
        if window.is_null() {DeleteObject(background);return;}
        title_theme(window,dark);
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
            else {state.status("Локальный установщик не найден. Выбери папку или источник загрузки.");}
            child("BUTTON","Яндекс Диск",WS_GROUP|WS_TABSTOP|BS_AUTORADIOBUTTON as u32,105,20,154,185,29);
            child("BUTTON","Google Диск",BS_AUTORADIOBUTTON as u32,106,225,154,185,29);
            child("BUTTON","Microsoft",BS_AUTORADIOBUTTON as u32,107,430,154,185,29);
            CheckRadioButton(window,105,107,105);
            state.install.set(child("BUTTON","Установить",WS_GROUP|WS_TABSTOP|BS_OWNERDRAW as u32,101,20,200,144,40));
            let available=state.selected.borrow().is_some(); EnableWindow(state.install.get(),available as i32);
            child("BUTTON","Выбрать папку…",WS_TABSTOP|BS_OWNERDRAW as u32,102,174,200,144,40);
            state.download_button.set(child("BUTTON","Скачать…",WS_TABSTOP|BS_OWNERDRAW as u32,103,328,200,144,40));
        }
        child("BUTTON","Закрыть",WS_TABSTOP|BS_OWNERDRAW as u32,104,482,if can_install {200} else {164},144,40);
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