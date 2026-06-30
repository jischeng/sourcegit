using System;
using System.Collections.Generic;
using System.IO;

namespace SourceGit.Remote
{
    /// <summary>
    /// Reads host aliases from the user's ~/.ssh/config so the SSH Remote dialog can offer
    /// them in a dropdown (carrying ProxyJump/identity/agent/passwordless as configured).
    /// </summary>
    public static class SshConfigParser
    {
        public static List<string> GetHosts()
        {
            var hosts = new List<string>();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
            if (!File.Exists(path))
                return hosts;

            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("Host ", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var name in line.Substring(5).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (name.Contains('*') || name.Contains('?'))
                            continue;
                        if (!hosts.Contains(name))
                            hosts.Add(name);
                    }
                }
            }
            catch
            {
                // ignore
            }

            return hosts;
        }
    }
}
