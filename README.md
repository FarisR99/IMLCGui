# IMLCGui

Lightweight GUI wrapper for [Intel Memory Latency Checker](https://www.intel.com/content/www/us/en/developer/articles/tool/intelr-memory-latency-checker.html).

![IMLCGui](https://user-images.githubusercontent.com/3731915/149428525-a9370dcd-330b-40fa-a24e-979067ba0647.png)

## Compatibility
Requires a Windows system with at least .NET Framework 4.6.2.

## Usage
1. Either download the compiled executable from [Releases](../../releases), or compile this project yourself (Visual Studio and `msbuild /t:ILRepack`).
2. Move the executable file to its own directory as it creates a configuration file and a log file.
4. Run the application as an administrator (not required but strongly recommended)
5. Click the Configure tab and either hit Download (attempts to download and extract Intel MLC to the directory MLC is running from) or Browse to locate your existing download of Intel MLC. This step is not necessary if you move your existing MLC files to the same directory that this executable is located in.

## Credits
<a href="https://www.flaticon.com/free-icons/ram" title="ram icons">Ram icons created by Freepik - Flaticon</a>
