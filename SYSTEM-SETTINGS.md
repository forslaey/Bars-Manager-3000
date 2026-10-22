# Системные настройки: профиль BARS и стандарт Windows

Профиль применения повторяет два согласованных скриншота. Возврат — к заводским значениям Windows, а не к состоянию до запуска менеджера. Доменная GPO/MDM может переопределить локальное значение. Доступ к доменным политикам и изменение состава администраторов не выполняются.

## Пароли

| Параметр | BARS | Стандарт автономной Windows |
|---|---:|---:|
| Аудит длины | Не определено | Не определено |
| История | 0 | 0 |
| Максимальный возраст | 0 / бессрочно | 42 дня |
| Минимальная длина | 0 | 0 |
| Минимальный возраст | 0 | 0 |
| Ослабить ограничение длины | Включено | Отключено |
| Сложность | Отключено | Отключено |
| Обратимое шифрование | Отключено | Отключено |

Шесть классических заводских значений извлекаются из `%windir%\inf\defltbase.inf` конкретной Windows, с проверкой наличия каждого ключа. Весь шаблон не применяется: создаётся INF только с этими шестью параметрами. Возраст «бессрочно» у secedit записывается как -1, в оснастке Windows показан 0. Остальные две политики обслуживаются только если доступны в данной ОС. На контроллере домена применение/сброс этой группы запрещены; существующие пароли не меняются.

## UAC

| Имя параметра Windows | BARS | Стандарт |
|---|---:|---:|
| ConsentPromptBehaviorEnhancedAdmin | 1 | 1 |
| EnableVirtualization | 0 | 1 |
| EnableLUA | 0 | 1 |
| TypeOfAdminApprovalMode | 1 | 1 |
| EnableInstallerDetection | 0 | 1 |
| PromptOnSecureDesktop | 0 | 1 |
| ConsentPromptBehaviorAdmin | 0 | 5 |
| ConsentPromptBehaviorUser | 3 | 3 |
| EnableSecureUIAPaths | 0 | 1 |
| ValidateAdminCodeSignatures | 0 | 0 |
| EnableUIADesktopToggle | 0 | 0 |
| EnableFullTokenForRemoteAuth | 0 | 0 |
| RemoteAdminAllowList | Не определено | Не определено |
| FilterAdministratorToken | 0 | 0 |

Новые параметры проверяются по определениям локальной оснастки Windows (`wsecedit.dll`/`sceregvl.inf`), а не создаются безусловно на Server или старых Windows. Неопределённое значение означает отсутствие параметра. Локальная запись проверяется после изменения. Отключение UAC не выдаёт права SYSTEM/TrustedInstaller и не превращает обычную учётную запись в администратора. Для эффективного изменения режима необходима перезагрузка. Здесь используется стандарт Windows, не корпоративный Microsoft Security Baseline (это разные профили).

## Брандмауэр / Defender

Брандмауэр: Domain, Private, Public переключаются через PersistentStore с последующей проверкой ActiveStore. Служба брандмауэра не останавливается, правила не удаляются. Обратная кнопка включает все три профиля.
Defender: переключается только защита в реальном времени, затем проверяется RealTimeProtectionEnabled. Защита от изменений не обходится; отказ возвращается как ошибка. Windows может самостоятельно включать временно отключённую защиту. Другие функции Defender и сторонние антивирусы не отключаются.

## Источники Microsoft

- [Общая конфигурация UAC и значения](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/user-account-control/settings-and-configuration)
- [CSP LocalPoliciesSecurityOptions: значения, в том числе новые политики](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-localpoliciessecurityoptions)
- [Administrator protection: применение и ограничения](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/administrator-protection/)
- [Обнаружение установки: заводское значение Enabled](https://learn.microsoft.com/en-us/previous-versions/windows/it-pro/windows-10/security/threat-protection/security-policy-settings/user-account-control-detect-application-installations-and-prompt-for-elevation)
- [Политики паролей](https://learn.microsoft.com/windows/security/threat-protection/security-policy-settings/password-policy)
- [Новые параметры длины пароля и поддерживаемые версии](https://support.microsoft.com/en-US/servicing/os/windows/2020/08/minimum-password-length-auditing-and-enforcement-on-certain-versions-of-windows)
- [Временное отключение защиты в реальном времени](https://support.microsoft.com/en-au/windows/security/threat-malware-protection/virus-and-threat-protection-in-the-windows-security-app)

Проверено 22.10.2026. Статические значения не подменяют требования организации; новые будущие редакции/политики могут требовать обновления профиля.
