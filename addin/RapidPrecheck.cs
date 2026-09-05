using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotStudioMcpAddin
{
    // A deliberately limited structural check, not a RAPID compiler.
    public static class RapidPrecheck
    {
        public sealed class Issue
        {
            public int line;
            public string message;
        }

        public static List<Issue> Check(string source)
        {
            var issues = new List<Issue>();
            var clean = new StringBuilder();
            bool comment = false, quoted = false;
            int line = 1, quoteLine = 1;
            foreach (char c in source)
            {
                if (c == '\n')
                {
                    if (quoted) issues.Add(new Issue { line = quoteLine, message = "Unclosed string literal." });
                    line++; comment = false; quoted = false; clean.Append(c); continue;
                }
                if (comment) { clean.Append(' '); continue; }
                if (c == '"') { quoted = !quoted; quoteLine = line; clean.Append(' '); continue; }
                if (!quoted && c == '!') comment = true;
                clean.Append(quoted || comment ? ' ' : c);
            }
            if (quoted) issues.Add(new Issue { line = quoteLine, message = "Unclosed string literal." });
            var pairs = new Dictionary<string, string> {
                { "MODULE", "ENDMODULE" }, { "PROC", "ENDPROC" }, { "FUNC", "ENDFUNC" },
                { "TRAP", "ENDTRAP" }, { "RECORD", "ENDRECORD" }, { "IF", "ENDIF" },
                { "FOR", "ENDFOR" }, { "WHILE", "ENDWHILE" }, { "TEST", "ENDTEST" }
            };
            var closers = new HashSet<string>(pairs.Values);
            var stack = new Stack<Tuple<string, int>>();
            var text = clean.ToString();
            var tokens = Regex.Matches(text, @"[A-Za-z_][A-Za-z_0-9]*|;|\n");
            line = 1;
            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i].Value.ToUpperInvariant();
                if (token == "\n") { line++; continue; }
                if (pairs.ContainsKey(token))
                {
                    if (token == "IF")
                    {
                        bool block = false;
                        for (int j = i + 1; j < tokens.Count && tokens[j].Value != ";"; j++)
                            if (tokens[j].Value.Equals("THEN", StringComparison.OrdinalIgnoreCase)) { block = true; break; }
                        if (!block) continue;
                    }
                    stack.Push(Tuple.Create(token, line));
                }
                else if (closers.Contains(token))
                {
                    if (stack.Count == 0 || pairs[stack.Peek().Item1] != token)
                        issues.Add(new Issue { line = line, message = "Unexpected " + token + "." });
                    else stack.Pop();
                }
            }
            foreach (var entry in stack)
                issues.Add(new Issue { line = entry.Item2, message = "Missing " + pairs[entry.Item1] + "." });
            if (string.IsNullOrWhiteSpace(source)) issues.Add(new Issue { line = 1, message = "Source is empty." });
            return issues;
        }
    }
}
