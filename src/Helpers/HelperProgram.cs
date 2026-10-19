using System;

namespace ServiceTool
{
    internal static class HelperProgram
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // Invalid input returns before opening pipes, loading service code or touching SCM.
            string pipe; int parent;
            if (!ServiceProfileInstaller.TryParseHelperArguments(args, out pipe, out parent)) return 87;
            try
            {
                using (EmbeddedHelpers.HoldHelperSession(System.Reflection.Assembly.GetExecutingAssembly().Location))
                    return ServiceProfileInstaller.RunHelper(args);
            }
            catch (Exception) { return 5; }
        }
    }
}
