# PoE Loading Screen Crash Fixer

This is a simple, light-weight, tool to fix PoE 2 causing Windows to freeze completely (or only be able to move your mouse) during a loading screen. This seems to mostly, but not only, affect AMD X3D CPUs.

## Table of Contents

1. [How it works](#how-it-works)
2. [How to Use](#how-to-use)
3. [FAQ](#faq)
   - [I open the process, but nothing seems to happen.](#i-open-the-process-but-nothing-seems-to-happen)
   - [Windows Defender is detecting this as a virus](#windows-defender-is-detecting-this-as-a-virus)
   - [Is this allowed?](#is-this-allowed)
   - [How do I know if it's working?](#how-do-i-know-if-its-working)

## How it works

People on the forums/Reddit discovered that if, after launching PoE 2, you go into Task Manager and remove CPU0/CPU1 from the process affinity for the PoE2 executable, the crashes go away.
That involves doing it every time you launch PoE2 though, and it also prevents your main 2 cores from being used at all times instead of just during loading screen.

What this tool does is detects when you're starting a loading screen (by reading client.txt), then changing the process affinity to not use CPU0/CPU1. Once the loading screen is complete
(again, discovers this by reading client.txt), it updates the affinity to be able to use those cores again.

This causes Windows to no longer freeze up, but it looks like for some people the game still freezes instead. I've found that when this happens, if you set the PoE process priority to realtime,
you can make PoE recover and unfreeze. For people who still experience the game (but not their PC) freezing after using this tool, run it as an administrator and it will attempt to recover
PoE by setting the process to realtime when it detects the game freezes. After the game recovers, it reverts it back to normal priority.

**If your issue is not Windows freezing on a loading screen, this tool will do nothing for you.**

## Intel CPU 13th and 14th generation crash to desktop support

Despite intel fixes for 13th and 14th gen cpus, PoE2 crashes to desktop for some people with a "Failed to decompress corrupted data". This happens during loading into a different zone, or while loading after starting the game.
This fix prevents the crash by changing into powersave mode during loading into a different zone. When you run this application you need to run it with a "-ctd" parameter, or use the provided .bat file that does the same.
The first time you run it you need to choose your powerplans to use as instructed.

## How to Use

1. Grab the release from the [releases page](https://github.com/Kapps/PoEUncrasher/releases/)
2. Unzip it
3. Run the executable there. It doesn't matter if you run it before or after PoE starts.
4. If you have issues where PoE still freezes but your computer doesn't, run the executable as an administrator instead. That should enable it to recover PoE.

## FAQ

### I open the process, but nothing seems to happen.
Make sure you have .NET Framework 9 installed, specifically the 64-bit SDK (probably just the runtime is fine, but I haven't tested it yet). Download it from [this link](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-9.0.101-windows-x64-binaries).

### Windows Defender is detecting this as a virus
The version that doesn't require .NET (`PoEUncrasher.-.Non.NET.zip`) runs into issues with false positives. It seems to be a [known issue](https://github.com/dotnet/runtime/issues/33745) with .NET/Windows Defender. Make sure you have .NET 9 installed (see above) and use the .NET version (`PoE.Uncrasher.zip`) instead.

### Is this allowed?
The tool does not interact with the game client in any way that's not allowed. It reads from the client.txt file, which is explicitly allowed by GGG. Other than that, all it does is modify the process affinity/priority exactly as if you were using the task manager. This means it should be completely fine to use.

### How do I know if it's working?
You should see something like this. If you don't see any console window after opening the program, make sure you have .NET 9 installed.
![image](https://github.com/user-attachments/assets/03e95e15-bbb1-4ebd-8c2f-345c37ab4643)
