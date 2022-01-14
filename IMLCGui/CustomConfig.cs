using System;
using System.Collections.Generic;
using System.Linq;

namespace IMLCGui
{
    // Taken from https://stackoverflow.com/questions/485659/can-net-load-and-parse-a-properties-file-equivalent-to-java-properties-class
    internal class CustomConfig
    {
        private Dictionary<string, string> list;
        private string filename;

        public CustomConfig(string file)
        {
            this.Reload(file);
        }

        public string Get(string field, string defValue)
        {
            string value = this.Get(field);
            return value != null ? value : defValue;
        }

        public string Get(string field)
        {
            return this.list.ContainsKey(field) ? this.list[field] : null;
        }

        public void Set(string field, string value)
        {
            if (!this.list.ContainsKey(field))
                this.list.Add(field, value.ToString());
            else
                this.list[field] = value.ToString();
        }

        public void Save()
        {
            this.Save(this.filename);
        }

        public void Save(string filename)
        {
            this.filename = filename;

            if (!System.IO.File.Exists(filename))
                System.IO.File.Create(filename).Close();

            System.IO.StreamWriter file = new System.IO.StreamWriter(filename);

            foreach (string prop in list.Keys.ToArray())
                if (!String.IsNullOrWhiteSpace(list[prop]))
                    file.WriteLine(prop + "=" + list[prop]);

            file.Close();
        }

        public void Reload()
        {
            Reload(this.filename);
        }

        public void Reload(string filename)
        {
            this.filename = filename;
            this.list = new Dictionary<string, string>();

            if (System.IO.File.Exists(filename))
                this.LoadFromFile(filename);
        }

        private void LoadFromFile(string file)
        {
            foreach (string line in System.IO.File.ReadAllLines(file))
            {
                if ((!String.IsNullOrEmpty(line)) &&
                    (!line.StartsWith(";")) &&
                    (!line.StartsWith("#")) &&
                    (!line.StartsWith("'")) &&
                    (line.Contains('=')))
                {
                    int index = line.IndexOf('=');
                    string key = line.Substring(0, index).Trim();
                    string value = line.Substring(index + 1).Trim();

                    if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                        (value.StartsWith("'") && value.EndsWith("'")))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }

                    try
                    {
                        this.list.Add(key, value);
                    }
                    catch { }
                }
            }
        }
    }

}
