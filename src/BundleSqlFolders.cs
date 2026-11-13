using System;
using System.Collections.Generic;

using System.Linq;


namespace ServiceTool
{
    internal static class BundleSqlFolders
    {
        internal static string Name(BundleRemoteFile file)
        {
            if (file.Group != "SQL") return null;
            var parts = file.Path.Split('/');
            int index = parts[1].Equals("_soft", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
            return parts.Length > index + 1 ? parts[index] : null;
        }

        internal static bool Includes(BundleRemoteFile file, ISet<string> selection)
        {
            string name = Name(file);
            return name != null && selection != null && selection.Contains(name);
        }

        internal static string[] Names(IEnumerable<BundleRemoteFile> files)
        {
            return files.Select(Name).Where(name => name != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }

    internal sealed class BundleSqlFoldersForm : BundleDifferencesForm
    {
        readonly BundleDifferenceItem[] rows;

        internal HashSet<string> Selected
        {
            get { return new HashSet<string>(rows.Where(row => row.Selected).Select(row => row.Path), StringComparer.OrdinalIgnoreCase); }
        }

        internal BundleSqlFoldersForm(IEnumerable<string> names, ISet<string> selection,string title=null)
            : this(names.Select(name => new BundleDifferenceItem
            {
                Path = name,
                Allowed = true,
                Selected = selection != null && selection.Contains(name)
            }).ToArray(),title)
        {
        }

        private BundleSqlFoldersForm(BundleDifferenceItem[] rows,string title) : base(rows, sql: true,pickerTitle:title)
        {
            this.rows = rows;
        }
    }
    internal sealed class ToolsPickerForm : BundleDifferencesForm
    {
        private readonly BundleDifferenceItem[] rows;
        internal ToolsPickerForm(IEnumerable<string> packages)
            : this(packages.Select(name=>new BundleDifferenceItem
            {
                Path=name, Allowed=true, Selected=true,
                ShortcutAvailable=ToolsDeployment.ShortcutExecutable(name)!=null
            }).ToArray()) {}
        private ToolsPickerForm(BundleDifferenceItem[] rows)
            : base(rows,sql:true,pickerTitle:"Наборы для распаковки",shortcuts:true) { this.rows=rows; }
        internal string[] Packages { get { return rows.Where(row=>row.Selected).Select(row=>row.Path).ToArray(); } }
        internal HashSet<string> Shortcuts { get { return new HashSet<string>(rows.Where(row=>row.Selected&&row.Shortcut).Select(row=>row.Path),StringComparer.OrdinalIgnoreCase); } }
    }
}