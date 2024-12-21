# PoE Loading Screen Crash Fixer

This is a simple, light-weight, tool to fix PoE 2 causing Windows to freeze completely (or only be able to move your mouse) during a loading screen. This seems to only affect AMD X3D CPUs from what I can tell.

## How it works

People on the forums/Reddit discovered that if, after launching PoE 2, you go into Task Manager and remove CPU0/CPU1 from the process affinity for the PoE2 executable, the crashes go away.
That involves doing it every time you launch PoE2 though, and it also prevents your main 2 cores from being used at all times instead of just during loading screen.

What this tool does is detects when you're starting a loading screen (by reading client.txt), then changing the process affinity to not use CPU0/CPU1. Once the loading screen is complete
(again, discovers this by reading client.txt), it updates the affinity to be able to use those cores again.

This causes Windows to no longer freeze up, but it looks like for some people the game still freezes instead. I've found that when this happens, if you set the PoE process priority to realtime,
you can make PoE recover and unfreeze. For people who still experience the game (but not their PC) freezing after using this tool, run it as an administrator and it will attempt to recover
PoE by setting the process to realtime when it detects the game freezes. After the game recovers, it reverts it back to normal priority.

**If your issue is not Windows freezing on a loading screen, this tool will do nothing for you.**

## How to Use

1. Grab the release from the [releases page](https://github.com/Kapps/PoEUncrasher/releases/)
2. Unzip it
3. Run the executable there. It doesn't matter if you run it before or after PoE starts.
4. If you have issues where PoE still freezes but your computer doesn't, run the executable as an administrator instead. That should cause it to recover PoE.

## Is this Allowed?

The tool does not interact with the game client in any way that's not allowed. It reads from the client.txt file, which is explicitly allowed by GGG. Other than that, all it does is modify the process affinity/priority exactly as if you were using the task manager. This means it should be completely fine to use.

## FAQ

### I open the process, but nothing seems to happen.
Make sure you have .NET Framework 9 installed, specifically the 64-bit SDK (probably just the runtime is fine, but I haven't tested it yet). Download it from [this link](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-9.0.101-windows-x64-binaries).

### Windows Defender is detecting this as a virus
The version that doesn't require .NET (`PoEUncrasher.-.Non.NET.zip`) runs into issues with false positives. It seems to be a [known issue](https://github.com/dotnet/runtime/issues/33745) with .NET/Windows Defender. Make sure you have .NET 9 installed (see above) and use the .NET version (`PoE.Uncrasher.zip`) instead.
