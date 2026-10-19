using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ServiceTool
{
    internal enum ServiceInstallMode { Ordinary, ManagedDotNet }
    internal enum InstallAccountKind { LocalService, NetworkService, LocalSystem, User }

    internal sealed class ServiceInstallProfile
    {
        public string ExecutablePath;
        public ServiceInstallMode Mode;
        public InstallAccountKind Account = InstallAccountKind.LocalSystem;
        public string UserName = String.Empty;
        // DPAPI CurrentUser ciphertext only. Never place the password in this field.
        public string ProtectedPassword = String.Empty;

        public static ServiceInstallProfile Default(string executable)
        {
            return new ServiceInstallProfile { ExecutablePath = NormalizeExecutable(executable) };
        }

        public ServiceInstallProfile Clone()
        {
            return new ServiceInstallProfile { ExecutablePath = ExecutablePath, Mode = Mode,
                Account = Account, UserName = UserName, ProtectedPassword = ProtectedPassword };
        }

        public void Validate()
        {
            ExecutablePath = NormalizeExecutable(ExecutablePath);
            if (!Enum.IsDefined(typeof(ServiceInstallMode), Mode) || !Enum.IsDefined(typeof(InstallAccountKind), Account))
                throw new ArgumentException("Неизвестный режим установки или тип учётной записи.");
            UserName = (UserName ?? String.Empty).Trim();
            if (UserName.Length > 512 || UserName.IndexOf('\0') >= 0 || UserName.IndexOf('\r') >= 0 || UserName.IndexOf('\n') >= 0)
                throw new ArgumentException("Проверь имя учётной записи службы.");
            if (Mode == ServiceInstallMode.ManagedDotNet && Account == InstallAccountKind.User && UserName.Length == 0)
                throw new ArgumentException("Введи учётную запись: КОМПЬЮТЕР\\пользователь или ДОМЕН\\пользователь.");
            ProtectedPassword = ProtectedPassword ?? String.Empty;
            if (ProtectedPassword.Length > 32768) throw new ArgumentException("Сохранённый пароль имеет недопустимый размер.");
            if (ProtectedPassword.Length > 0) Convert.FromBase64String(ProtectedPassword);
            if (Mode == ServiceInstallMode.Ordinary || Account != InstallAccountKind.User)
            {
                UserName = String.Empty;
                ProtectedPassword = String.Empty;
            }
        }

        public bool HasSavedPassword { get { return !String.IsNullOrEmpty(ProtectedPassword); } }

        public void RememberPassword(string password)
        {
            if (Mode != ServiceInstallMode.ManagedDotNet || Account != InstallAccountKind.User)
                throw new InvalidOperationException("Пароль можно сохранить только для пользовательской учётной записи.");
            if (password == null || password.Length == 0 || password.Length > 4096 || password.IndexOf('\0') >= 0)
                throw new ArgumentException("Введи пароль учётной записи Windows.");
            byte[] plain = Encoding.UTF8.GetBytes(password);
            try { ProtectedPassword = Convert.ToBase64String(ProtectedData.Protect(plain, Entropy(), DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public bool TryGetSavedPassword(out string password, out string warning)
        {
            password = null;
            warning = String.Empty;
            if (!HasSavedPassword) return false;
            byte[] plain = null;
            try
            {
                plain = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword), Entropy(), DataProtectionScope.CurrentUser);
                password = new UTF8Encoding(false, true).GetString(plain);
                if (password.Length == 0 || password.Length > 4096 || password.IndexOf('\0') >= 0) throw new CryptographicException();
                return true;
            }
            catch (Exception ex)
            {
                if (!(ex is CryptographicException) && !(ex is FormatException) && !(ex is ArgumentException)) throw;
                password = null;
                warning = "Сохранённый пароль не удалось расшифровать. Введи его заново в окне установки.";
                return false;
            }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }

        private byte[] Entropy()
        {
            using (SHA256 hash = SHA256.Create())
                return hash.ComputeHash(Encoding.UTF8.GetBytes("BARS.ServiceManager3000.InstallProfile.1\n" + NormalizeExecutable(ExecutablePath).ToUpperInvariant() +
                    "\n" + (UserName ?? String.Empty).Trim().ToUpperInvariant()));
        }

        internal static string NormalizeExecutable(string executable)
        {
            string path = ExecutablePaths.Normalize(executable);
            if (!String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Выбери .exe службы BARS.");
            return path;
        }
    }

    internal sealed class ServiceInstallProfileStore
    {
        private readonly object gate = new object();
        private readonly string storagePath;
        private readonly bool memory;
        private bool writesDisabled;
        private Dictionary<string, ServiceInstallProfile> profiles = NewMap();
        private readonly Dictionary<string, ServiceInstallProfile> pending = NewMap();
        public string Warning { get; private set; }

        public ServiceInstallProfileStore(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Не указан файл настроек установки.");
            storagePath = Path.GetFullPath(path);
            Warning = String.Empty;
            try { profiles = Read(storagePath); }
            catch (Exception ex)
            {
                if (!StorageError(ex)) throw;
                writesDisabled = true;
                Warning = "Не удалось прочитать настройки установки. Исходный файл не изменён; сохранение отключено. " + ex.Message;
            }
        }

        private ServiceInstallProfileStore() { memory = true; Warning = String.Empty; }
        public static ServiceInstallProfileStore CreateMemory() { return new ServiceInstallProfileStore(); }
        public static ServiceInstallProfileStore CreateDefault()
        {
            return new ServiceInstallProfileStore(ApplicationStorage.FilePath("install-profiles.xml"));
        }

        public ServiceInstallProfile Get(string executable)
        {
            string path = ServiceInstallProfile.NormalizeExecutable(executable);
            lock (gate)
            {
                ServiceInstallProfile profile;
                if (profiles.TryGetValue(path, out profile)) return profile.Clone();
                profile = ServiceInstallProfile.Default(path);
                if (String.Equals(Path.GetFileName(path), "HttpService.exe", StringComparison.OrdinalIgnoreCase))
                {
                    profile.Mode = ServiceInstallMode.ManagedDotNet;
                }
                return profile;
            }
        }

        public bool HasStoredProfile(string executable)
        {
            string path = ServiceInstallProfile.NormalizeExecutable(executable);
            lock (gate) return profiles.ContainsKey(path);
        }

        // Settings stay usable for this session if disk persistence fails.
        public bool Save(ServiceInstallProfile value)
        {
            if (value == null) throw new ArgumentNullException("value");
            ServiceInstallProfile profile = value.Clone();
            profile.Validate();
            lock (gate)
            {
                profiles[profile.ExecutablePath] = profile;
                pending[profile.ExecutablePath] = profile;
                if (memory) return true;
                if (writesDisabled) return false;
                string temporary = null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(storagePath));
                    using (FileStream fileLock = new FileStream(storagePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        Dictionary<string, ServiceInstallProfile> merged = Read(storagePath);
                        foreach (KeyValuePair<string, ServiceInstallProfile> change in pending) merged[change.Key] = change.Value;
                        if (merged.Count > 1000) throw new IOException("Слишком много профилей установки.");
                        temporary = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        using (XmlWriter writer = XmlWriter.Create(temporary, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
                        {
                            writer.WriteStartElement("installProfiles"); writer.WriteAttributeString("version", "1");
                            var keys = new List<string>(merged.Keys); keys.Sort(StringComparer.OrdinalIgnoreCase);
                            foreach (string key in keys)
                            {
                                ServiceInstallProfile item = merged[key];
                                writer.WriteStartElement("profile");
                                writer.WriteAttributeString("exe", item.ExecutablePath);
                                writer.WriteAttributeString("mode", item.Mode.ToString());
                                writer.WriteAttributeString("account", item.Account.ToString());
                                writer.WriteAttributeString("username", item.UserName);
                                if (item.HasSavedPassword) writer.WriteAttributeString("protectedPassword", item.ProtectedPassword);
                                writer.WriteEndElement();
                            }
                            writer.WriteEndElement();
                        }
                        if (File.Exists(storagePath)) File.Replace(temporary, storagePath, null);
                        else File.Move(temporary, storagePath);
                        temporary = null;
                        profiles = merged;
                        pending.Clear();
                        Warning = String.Empty;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    if (!StorageError(ex)) throw;
                    Warning = "Настройки установки действуют в этом сеансе, но сохранить их не удалось. " + ex.Message;
                    return false;
                }
                finally { if (temporary != null) try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }

        private static Dictionary<string, ServiceInstallProfile> NewMap()
        {
            return new Dictionary<string, ServiceInstallProfile>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, ServiceInstallProfile> Read(string path)
        {
            var result = NewMap();
            if (!File.Exists(path)) return result;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 1024 * 1024) throw new XmlException("Файл настроек слишком большой.");
                XmlDocument document = new XmlDocument { XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 })) document.Load(reader);
                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "installProfiles" || root.GetAttribute("version") != "1")
                    throw new XmlException("Неизвестный формат настроек установки.");
                foreach (XmlNode node in root.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Whitespace || node.NodeType == XmlNodeType.Comment) continue;
                    XmlElement item = node as XmlElement;
                    if (item == null || item.Name != "profile" || item.HasChildNodes) throw new XmlException("Некорректный профиль установки.");
                    ServiceInstallMode mode; InstallAccountKind account;
                    if (!Enum.TryParse<ServiceInstallMode>(item.GetAttribute("mode"), false, out mode) ||
                        !Enum.TryParse<InstallAccountKind>(item.GetAttribute("account"), false, out account))
                        throw new XmlException("Неизвестный режим профиля установки.");
                    var profile = new ServiceInstallProfile { ExecutablePath = item.GetAttribute("exe"), Mode = mode,
                        Account = account, UserName = item.GetAttribute("username"), ProtectedPassword = item.GetAttribute("protectedPassword") };
                    profile.Validate();
                    if (result.ContainsKey(profile.ExecutablePath) || result.Count >= 1000) throw new XmlException("Повторяющийся профиль установки или превышен лимит.");
                    result.Add(profile.ExecutablePath, profile);
                }
            }
            return result;
        }

        private static bool StorageError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is XmlException || ex is ArgumentException || ex is FormatException || ex is NotSupportedException;
        }
    }
}
