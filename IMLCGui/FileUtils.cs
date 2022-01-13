using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMLCGui
{
    internal class FileUtils
    {
        public static void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        public static bool DoesExist(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        public static string GetCurrentPath(string path)
        {
            return Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        public static string GetTempPath(string path)
        {
            return Path.Combine(Path.GetTempPath(), path);
        }

        public static void CopyAndMove(string source, string destination)
        {
            if (File.Exists(source))
            {
                File.Copy(source, destination);
                File.Delete(source);
            }
            else if (Directory.Exists(source))
            {
                DirectoryCopy(source, destination, true);
                Directory.Delete(source, true);
            }
        }

        public static void Move(string source, string destination)
        {
            FileInfo sourceInfo = new FileInfo(source);
            if (!sourceInfo.Exists)
            {
                return;
            }
            if (sourceInfo.Directory != null)
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }
        }

        // Taken from https://docs.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException("Source directory does not exist or could not be found: " + sourceDirName);
            }
            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }
    }
}
