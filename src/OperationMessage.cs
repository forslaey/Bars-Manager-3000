using System;

namespace ServiceTool
{
    internal static class OperationMessage
    {
        internal static string FinishSentence(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return String.Empty;
            text = text.TrimEnd();
            return ".…!?".IndexOf(text[text.Length - 1]) >= 0 ? text : text + ".";
        }
    }
}
