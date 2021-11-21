using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meadow.Helpers
{
    public class MeadowSettings
    {
        protected const string separator = "||";

        public MeadowSettings(string path, bool autoLoad = true)
        {
            Path = path;
            if (autoLoad) Load();
        }

        public string Path { get; protected set; }
        public string DeviceTarget { get; set; }

        public void Load()
        {
            if (File.Exists(this.Path))
            {
                var settings = File.ReadAllLines(this.Path);
                if(settings.Count() == 1)
                {
                    if(settings[0].IndexOf(separator)> 0)
                    {
                        DeviceTarget = settings[0].Split(new string[] { separator }, StringSplitOptions.None)[1];
                    }
                }
            }
        }

        public void Save()
        {
            var content = new List<string>
            {
                $"DeviceTarget{separator}{DeviceTarget}",
            };

            var dir = new FileInfo(this.Path).DirectoryName;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllLines(this.Path, content);
        }
    }
}
