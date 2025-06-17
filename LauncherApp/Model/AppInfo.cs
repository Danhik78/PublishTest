using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LauncherApp.Model
{
    internal class AppInfo
    {
        public string Name { get; set; }
        public string ExePath { get; set; }  // Путь к EXE на диске
        public Version CurrentVersion { get; set; }
        public string GitHubRepo { get; set; }  // Например: "username/reponame"
    }
}
