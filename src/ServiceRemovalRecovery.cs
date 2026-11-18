using System;
using System.Collections.Generic;
using System.Linq;

namespace ServiceTool
{
    internal static class ServiceRemovalRecovery
    {
        internal static ServiceEntry Resolve(ServiceSnapshot snapshot,string executable,
            IEnumerable<string> previousNames,string selectedName)
        {
            if(snapshot==null||!snapshot.Complete||snapshot.Entries==null)
                throw new InvalidOperationException("Не удалось полностью проверить службы. Обнови статус перед удалением регистрации.");
            var matches=ServiceCatalog.FindMatches(snapshot.Entries,executable);
            if(matches.Count==0)return null;
            var previous=new HashSet<string>(previousNames,StringComparer.OrdinalIgnoreCase);
            ServiceEntry target=matches.Count==1?matches[0]:
                matches.SingleOrDefault(entry=>String.Equals(entry.Name,selectedName,StringComparison.OrdinalIgnoreCase));
            if(target==null)
                throw new InvalidOperationException("Этот EXE связан с несколькими службами. Выбери нужную строку в таблице и повтори удаление.");
            if(!previous.Contains(target.Name))
                throw new InvalidOperationException("Регистрация службы изменилась. Обнови статус и повтори удаление.");
            return target;
        }

        internal static bool Removed(ServiceSnapshot snapshot,string executable,string targetName)
        {
            if(snapshot==null||!snapshot.Complete||snapshot.Entries==null)return false;
            return targetName==null?ServiceCatalog.FindMatches(snapshot.Entries,executable).Count==0:
                !snapshot.Entries.Any(entry=>String.Equals(entry.Name,targetName,StringComparison.OrdinalIgnoreCase));
        }
    }
}
