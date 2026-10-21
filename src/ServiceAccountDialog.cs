using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class ServiceAccountDialog : Form
    {
        private readonly ServiceInstallProfile original;
        private readonly bool installing;
        private readonly ThemePalette colors;
        private readonly ComboBox mode = new BrandComboBox();
        private readonly ComboBox account = new BrandComboBox();
        private readonly ComboBox username = new BrandComboBox();
        private readonly TextBox password = new BrandTextBox();
        private readonly RefreshToggle remember = new RefreshToggle();
        private readonly Label accountDetail = new FlushLabel();
        private readonly Label passwordDetail = new FlushLabel();
        private readonly Label error = new FlushLabel();
        private readonly Font titleFont = new Font("Segoe UI", 15F, FontStyle.Bold);
        private bool binding;
        private bool identityChanged;
        public ServiceInstallProfile Profile { get; private set; }
        public string Password { get; private set; }

        public ServiceAccountDialog(ThemePalette palette, ServiceInstallProfile profile, bool installing)
        {
            if (profile == null) throw new ArgumentNullException("profile");
            original = profile.Clone(); original.Validate();
            this.installing = installing;
            colors = palette ?? ThemePalette.Create(false);
            SuspendLayout();
            Text = installing ? "Установка с учётной записью" : "Настройки установки службы";
            Font = new Font("Segoe UI", 10F);
            BackColor = colors.Canvas; ForeColor = colors.Text;
            ClientSize = new Size(620, 576);
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 18),
                RowCount = 14, ColumnCount = 2, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            float[] heights = { 34, 30, 42, 12, 42, 58, 38, 38, 30, 52, 48, 22, 12, 42 };
            foreach (float height in heights) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Label title = MakeLabel(installing ? "Установка .NET-службы" : "Как устанавливать эту службу", colors.Text);
            title.Font = titleFont; layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
            Label file = MakeLabel(Path.GetFileName(original.ExecutablePath), colors.Muted);
            file.AutoEllipsis = true; layout.Controls.Add(file, 0, 1); layout.SetColumnSpan(file, 2);
            layout.Controls.Add(MakeLabel("Режим установки", colors.Muted), 0, 2);
            ConfigureCombo(mode);
            mode.Items.AddRange(new object[] { "Обычная (-i / -u)", ".NET с учётной записью" });
            layout.Controls.Add(mode, 1, 2);
            layout.Controls.Add(MakeLabel("Учётная запись", colors.Muted), 0, 4);
            ConfigureCombo(account);
            account.Items.AddRange(new object[] { "LocalService", "NetworkService", "LocalSystem", "Пользователь Windows" });
            layout.Controls.Add(account, 1, 4);
            accountDetail.Dock = DockStyle.Fill; accountDetail.ForeColor = colors.Muted;
            accountDetail.Margin = new Padding(0, 5, 0, 0);
            layout.Controls.Add(accountDetail, 0, 5); layout.SetColumnSpan(accountDetail, 2);
            layout.Controls.Add(MakeLabel("Имя пользователя", colors.Muted), 0, 6);
            ConfigureCombo(username);
            var userRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            userRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            userRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            userRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            userRow.Controls.Add(username, 0, 0);
            var refreshUsers = new BrandButton { Text = "↻", AccessibleName = "Обновить список пользователей Windows", Palette = colors,
                Dock = DockStyle.Fill, Margin = new Padding(5, 2, 0, 2) };
            refreshUsers.Click += delegate { LoadUsers(username.Text); };
            userRow.Controls.Add(refreshUsers, 1, 0);
            layout.Controls.Add(userRow, 1, 6);
            layout.Controls.Add(MakeLabel("Пароль Windows", colors.Muted), 0, 7);
            ConfigureTextBox(password); password.UseSystemPasswordChar = true; password.MaxLength = 4096;
            layout.Controls.Add(password, 1, 7);
            remember.Text = "Запомнить пароль на этом компьютере";
            remember.Dock = DockStyle.Fill; remember.Margin = Padding.Empty;
            remember.ForeColor = colors.Text; remember.BackColor = colors.Canvas;
            remember.Palette = colors;
            layout.Controls.Add(remember, 0, 8); layout.SetColumnSpan(remember, 2);
            passwordDetail.Dock = DockStyle.Fill; passwordDetail.ForeColor = colors.Muted;
            passwordDetail.Margin = new Padding(0, 14, 0, 0);
            layout.Controls.Add(passwordDetail, 0, 9); layout.SetColumnSpan(passwordDetail, 2);
            Label storage = MakeLabel("Сохраняются настройки только выбранного .exe. Пароль защищён Windows и доступен только твоей учётной записи.", colors.Muted);
            storage.Font = new Font("Segoe UI", 8.5F); storage.TextAlign = ContentAlignment.TopLeft;
            storage.Margin = new Padding(0, 12, 0, 0);
            layout.Controls.Add(storage, 0, 10); layout.SetColumnSpan(storage, 2);
            error.Dock = DockStyle.Fill; error.ForeColor = colors.Danger; error.AutoEllipsis = true;
            error.Font = new Font("Segoe UI", 8.5F); error.Margin = Padding.Empty;
            layout.Controls.Add(error, 0, 11); layout.SetColumnSpan(error, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Margin = Padding.Empty };
            var accept = new BrandButton { Text = installing ? "Установить" : "Сохранить", Palette = colors,
                Primary = true, Size = new Size(145, 40), Margin = Padding.Empty };
            var cancel = new BrandButton { Text = "Отмена", Palette = colors, Size = new Size(110, 40),
                Margin = new Padding(0, 0, 10, 0), DialogResult = DialogResult.Cancel };
            accept.Click += AcceptSettings;
            actions.Controls.Add(accept); actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 13); layout.SetColumnSpan(actions, 2);
            Controls.Add(layout); AcceptButton = accept; CancelButton = cancel;
            binding = true;
            mode.SelectedIndex = (int)original.Mode; account.SelectedIndex = (int)InstallAccountKind.LocalSystem;
            LoadUsers(original.UserName); remember.Checked = original.HasSavedPassword;
            binding = false;
            ForgetChangedIdentity();
            mode.Enabled = !installing;
            mode.SelectedIndexChanged += delegate { UpdateAvailability(); };
            account.SelectedIndexChanged += delegate { ForgetChangedIdentity(); UpdateAvailability(); };
            username.SelectedIndexChanged += delegate { ForgetChangedIdentity(); UpdateAvailability(); };
            UpdateAvailability(); ResumeLayout(true);
        }

        private Label MakeLabel(string text, Color color)
        {
            return new FlushLabel { Text = text, ForeColor = color, Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty, UseMnemonic = false };
        }

        private void ConfigureCombo(ComboBox combo)
        {
            combo.Dock = DockStyle.Fill; combo.Margin = new Padding(0, 5, 0, 5);
            combo.DropDownStyle = ComboBoxStyle.DropDownList; combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = colors.SurfaceAlt; combo.ForeColor = colors.Text;
            ((BrandComboBox)combo).Palette = colors;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                bool selected = (args.State & DrawItemState.Selected) != 0;
                Color background = selected ? colors.Selection : colors.SurfaceAlt;
                Color foreground = combo.Enabled ? (selected ? colors.SelectionText : colors.Text) : colors.DisabledText;
                using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
                string item = args.Index >= 0 ? Convert.ToString(combo.Items[args.Index]) : combo.Text;
                TextRenderer.DrawText(args.Graphics, item, combo.Font, new Rectangle(args.Bounds.X + 5, args.Bounds.Y,
                    Math.Max(1, args.Bounds.Width - 8), args.Bounds.Height), foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
            };
        }

        private void ConfigureTextBox(TextBox box)
        {
            box.Dock = DockStyle.Fill; box.Margin = new Padding(0, 5, 0, 5);
            box.BackColor = colors.Surface; box.ForeColor = colors.Text;
            box.BorderStyle = BorderStyle.FixedSingle;
            BrandTextBox input = box as BrandTextBox; if (input != null) input.Palette = colors;
        }

        private void ForgetChangedIdentity()
        {
            if (binding) return;
            password.Clear();
            identityChanged = account.SelectedIndex != (int)original.Account ||
                !String.Equals(username.Text.Trim(), original.UserName, StringComparison.OrdinalIgnoreCase);
            if (identityChanged) remember.Checked = false;
        }

        private void UpdateAvailability()
        {
            bool managed = mode.SelectedIndex == (int)ServiceInstallMode.ManagedDotNet;
            bool user = managed && account.SelectedIndex == (int)InstallAccountKind.User;
            account.Enabled = managed; username.Enabled = password.Enabled = remember.Enabled = user;
            string[] descriptions = {
                "LocalService: ограниченные локальные права; без учётных данных для сетевых ресурсов.",
                "NetworkService: ограниченные локальные права; доступ к сети от имени компьютера.",
                "LocalSystem: полный доступ к локальному компьютеру. Выбирай, только если это требуется службе.",
                "Выбери пользователя Windows и введи пароль этой учётной записи. В списке — локальные пользователи и твоя текущая учётная запись." };
            accountDetail.Text = managed && account.SelectedIndex >= 0 ? descriptions[account.SelectedIndex] :
                ".exe службы BARS сам выполняет установку с -i и удаление с -u.";
            passwordDetail.Text = user && original.HasSavedPassword && !identityChanged
                ? "Пароль уже сохранён. Оставь поле пустым, чтобы использовать его, или введи новый."
                : user ? "Пароль вводится здесь. Если не сохранять его, он понадобится при следующей установке."
                : "Для встроенных учётных записей пароль не нужен.";
            if (!managed) passwordDetail.Text = "Настройки самого установщика .exe используются без изменений.";
        }

        private void AcceptSettings(object sender, EventArgs e)
        {
            try
            {
                ServiceInstallProfile result = original.Clone();
                result.Mode = (ServiceInstallMode)mode.SelectedIndex;
                result.Account = (InstallAccountKind)account.SelectedIndex;
                result.UserName = username.Text.Trim();
                if (identityChanged) result.ProtectedPassword = String.Empty;
                result.Validate();
                if (result.Mode == ServiceInstallMode.ManagedDotNet && result.Account == InstallAccountKind.User)
                    WindowsAccountInfo.ValidateUserName(result.UserName);
                string entered = null;
                if (result.Mode == ServiceInstallMode.ManagedDotNet && result.Account == InstallAccountKind.User)
                {
                    entered = password.Text;
                    if (installing && entered.Length == 0)
                    {
                        string warning;
                        if (!result.TryGetSavedPassword(out entered, out warning))
                            throw new ArgumentException(String.IsNullOrEmpty(warning) ? "Введи пароль учётной записи Windows." : warning);
                    }
                    if (remember.Checked)
                    {
                        if (!String.IsNullOrEmpty(entered)) result.RememberPassword(entered);
                        else if (!result.HasSavedPassword) throw new ArgumentException("Чтобы запомнить пароль, сначала введи его.");
                    }
                    else result.ProtectedPassword = String.Empty;
                }
                Profile = result;
                Password = installing ? entered : null;
                password.Clear();
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is System.Security.Cryptography.CryptographicException)) throw;
                error.Text = ex is System.Security.Cryptography.CryptographicException ? "Windows не смогла защитить пароль. Сними флажок сохранения или повтори попытку." : ex.Message;
            }
        }

        private void LoadUsers(string selected)
        {
            try
            {
                List<string> users;
                try { users = WindowsAccountInfo.ListLocalUsers(); }
                catch (System.ComponentModel.Win32Exception)
                {
                    users = new List<string>();
                    string current = WindowsAccountInfo.CurrentUserName();
                    if (!String.IsNullOrWhiteSpace(current)) users.Add(current);
                    error.Text = "Windows не дала прочитать локальный список. Доступна твоя текущая учётная запись.";
                }
                // Preserve an explicitly saved domain account only after resolving its SID.
                if (!String.IsNullOrWhiteSpace(selected) && !users.Exists(delegate(string name) { return String.Equals(name, selected, StringComparison.OrdinalIgnoreCase); }))
                {
                    try { WindowsAccountInfo.ValidateUserName(selected); users.Add(selected); }
                    catch (ArgumentException ex) { error.Text = ex.Message; }
                }
                bool oldBinding = binding; binding = true;
                username.Items.Clear();
                foreach (string user in users) username.Items.Add(user);
                for (int i = 0; i < username.Items.Count; i++)
                    if (String.Equals((string)username.Items[i], selected, StringComparison.OrdinalIgnoreCase)) username.SelectedIndex = i;
                binding = oldBinding;
                if (!binding) { ForgetChangedIdentity(); UpdateAvailability(); }
            }
            catch (Exception ex)
            {
                if (!(ex is System.ComponentModel.Win32Exception) && !(ex is ArgumentException) && !(ex is System.Security.SecurityException)) throw;
                error.Text = "Список пользователей недоступен. Обнови его: " + ex.Message;
            }
        }

        protected override void Dispose(bool disposing)
        {
            Password = null;
            if (disposing) { password.Clear(); titleFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
