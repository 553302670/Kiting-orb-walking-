using LowLevelInput.Hooks;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kiting_orb_walking_
{
    public class Settings
    {
        public int ActivationKey { get; set; } = (int)VirtualKeyCode.Space;

        public void CreateNew(string path)
        {
            using (StreamWriter sw = new StreamWriter(File.Create(path)))
            {
                sw.WriteLine("/* \t键码对照表");
                foreach (int i in Enum.GetValues(typeof(VirtualKeyCode)))
                {
                    sw.WriteLine($"* \t{i} - {(VirtualKeyCode)i}");
                }
                sw.WriteLine("* \tActivationKey:走砍激活键");
                sw.WriteLine("*/");
                sw.WriteLine(JsonConvert.SerializeObject(this));
            }
        }

        public void Load(string path)
        {
            ActivationKey = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path)).ActivationKey;
        }
    }
}
