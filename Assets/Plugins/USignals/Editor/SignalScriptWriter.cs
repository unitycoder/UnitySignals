using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace USignals.EditorTools
{
    /// <summary>
    /// Writes receiver method stubs into the target component's C# file, the way Godot
    /// generates <c>_on_node_signal()</c> when you connect from the editor.
    /// Brace matching is textual, so it does not understand braces inside comments or strings.
    /// </summary>
    public static class SignalScriptWriter
    {
        public static MonoScript GetScript(Component component)
        {
            var behaviour = component as MonoBehaviour;
            if (behaviour == null) return null;

            var script = MonoScript.FromMonoBehaviour(behaviour);
            if (script == null) return null;

            var path = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return null;
            if (!path.StartsWith("Assets/")) return null;   // read-only package script

            return script;
        }

        public static bool AddMethod(Component target, string methodName, Type[] paramTypes, string[] paramNames,
                                     string signalDescription, bool openScript, out string error)
        {
            error = null;

            var script = GetScript(target);
            if (script == null) { error = "The target component has no editable script under Assets/."; return false; }

            var path = AssetDatabase.GetAssetPath(script);
            var text = File.ReadAllText(path);

            if (MethodExists(text, methodName))
            {
                if (openScript) OpenScriptAt(script, FindMethodLine(text, methodName));
                return true;   // nothing to write, the connection is still valid
            }

            var className = script.GetClass() != null ? script.GetClass().Name : Path.GetFileNameWithoutExtension(path);
            int insertAt = FindClassBodyEnd(text, className);
            if (insertAt < 0) { error = "Could not locate the body of class '" + className + "' in " + path + "."; return false; }

            // whitespace in front of the class' closing brace (non-zero when the class sits in a namespace)
            int lineStart = insertAt;
            while (lineStart > 0 && (text[lineStart - 1] == ' ' || text[lineStart - 1] == '\t')) lineStart--;
            var closingIndent = text.Substring(lineStart, insertAt - lineStart);

            var indent = closingIndent + DetectIndent(text);
            var body = BuildMethod(methodName, paramTypes, paramNames, signalDescription, indent, DetectIndent(text));

            var prefix = text.Substring(0, lineStart);
            if (!prefix.EndsWith("\n")) prefix += "\n";

            var updated = prefix + "\n" + body + closingIndent + text.Substring(insertAt);
            File.WriteAllText(path, updated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (openScript) OpenScriptAt(script, FindMethodLine(updated, methodName));
            return true;
        }

        public static void OpenMethod(SignalConnection connection)
        {
            if (connection == null || connection.target == null) return;
            var script = GetScript(connection.target);
            if (script == null) return;

            var path = AssetDatabase.GetAssetPath(script);
            var line = FindMethodLine(File.ReadAllText(path), connection.method);
            OpenScriptAt(script, line);
        }

        // -------------------------------------------------------------- internals

        static string BuildMethod(string name, Type[] paramTypes, string[] paramNames, string signalDescription,
                                  string indent, string step)
        {
            var sb = new StringBuilder();
            sb.Append(indent).Append("// Signal receiver — connected to ").Append(signalDescription).Append('\n');
            sb.Append(indent).Append("private void ").Append(name).Append('(');
            for (int i = 0; i < paramTypes.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(SignalUtility.CodeType(paramTypes[i])).Append(' ').Append(SafeParamName(paramNames[i], i));
            }
            sb.Append(")\n");
            sb.Append(indent).Append("{\n");
            sb.Append(indent).Append(step).Append("\n");
            sb.Append(indent).Append("}\n");
            return sb.ToString();
        }

        static string SafeParamName(string name, int index)
        {
            if (string.IsNullOrEmpty(name)) return "arg" + index;
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
                if (char.IsLetterOrDigit(name[i]) || name[i] == '_') sb.Append(name[i]);
            if (sb.Length == 0 || char.IsDigit(sb[0])) return "arg" + index;
            return sb.ToString();
        }

        static bool MethodExists(string text, string methodName)
        {
            return Regex.IsMatch(text, @"\b" + Regex.Escape(methodName) + @"\s*\(");
        }

        static int FindMethodLine(string text, string methodName)
        {
            var match = Regex.Match(text, @"\b" + Regex.Escape(methodName) + @"\s*\(");
            if (!match.Success) return 1;
            int line = 1;
            for (int i = 0; i < match.Index && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        /// <summary>Index of the class's closing brace.</summary>
        static int FindClassBodyEnd(string text, string className)
        {
            var declaration = Regex.Match(text, @"\b(class|struct)\s+" + Regex.Escape(className) + @"\b");
            if (!declaration.Success) return -1;

            int open = text.IndexOf('{', declaration.Index);
            if (open < 0) return -1;

            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        static string DetectIndent(string text)
        {
            var match = Regex.Match(text, @"\n(\t|[ ]+)\S");
            if (match.Success)
            {
                var indent = match.Groups[1].Value;
                // a method inside a namespace-less class is one level in; keep whatever the file uses
                return indent.Length > 8 ? "    " : indent;
            }
            return "    ";
        }

        static void OpenScriptAt(MonoScript script, int line)
        {
            AssetDatabase.OpenAsset(script, Mathf.Max(1, line));
        }
    }
}
