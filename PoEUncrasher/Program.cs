// See https://aka.ms/new-console-template for more information
#pragma warning disable CA1416

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoEUncrasher;

ServiceProvider serviceProvider = new ServiceCollection()
    .AddLogging((loggingBuilder) => loggingBuilder
        .SetMinimumLevel(LogLevel.Trace)
        .AddSimpleConsole(opts => {
            opts.SingleLine = true;
            opts.IncludeScopes = false;
            opts.TimestampFormat = "hh:mm:ss ";
        })
    )
    .BuildServiceProvider();

var logger = serviceProvider.GetService<ILoggerFactory>()!.CreateLogger<Program>();

int numCores = GetNumberProcessors();

int coresToPark = 2;
if (Int32.TryParse(Environment.GetEnvironmentVariable("CORES_TO_PARK"), out int coresToParkEnv)) {
    coresToPark = coresToParkEnv;
}

Regex startGameMatcher = new(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2} \d+ [a-fA-F0-9]+ \[INFO Client \d+\] \[ENGINE\] Init$", RegexOptions.Compiled);
Regex startLoadMatcher = new(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2} \d+ [a-fA-F0-9]+ \[INFO Client \d+\] \[SHADER\] Delay: OFF$", RegexOptions.Compiled);
Regex endLoadMatcher = new(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2} \d+ [a-fA-F0-9]+ \[INFO Client \d+\] \[SHADER\] Delay: ON", RegexOptions.Compiled);
Regex guidMatcher = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

bool isLoading = false;
bool ctdMode = false;

if (args.Length > 0)
{
    if (args[0] == "-ctd") ctdMode = true;   
}

var powerPlanPath = "powerplan.json";
PowerPlan? powerPlan = null;

var fallbackGamePath = @"C:\Program Files (x86)\Grinding Gear Games\Path of Exile 2"; 
var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, _) => cancellationSource.Cancel();

if (ctdMode)
{
    if (!File.Exists(powerPlanPath)) {
        await ChoosePowerPlan();
    }
    else {
        logger.LogInformation($"Delete {powerPlanPath} to reset your powerplan choices");
        powerPlan = DeserializePlan();
    }
}

PowerPlan? DeserializePlan()
{
    var fileText = File.ReadAllText(powerPlanPath);
    return JsonSerializer.Deserialize<PowerPlan>(fileText);
}

async Task ChoosePowerPlan()
{
    var lines = await GetWindowsPowerPlans();

    for (var i = 0; i < lines.Count; i++)
    {
        var line = lines[i];
        Console.WriteLine(i+1 + " " + line[line.IndexOf('(')..]);
    }
            
    Console.WriteLine("Please choose your energy saving power plan through the corresponging number:");
    var input = Console.ReadLine();
    var chosenplanPowersave = int.TryParse(input, out var chosenIndexSave) ? chosenIndexSave : -1;
    
    Console.WriteLine("Please choose your performance power plan through the corresponging number:");
    input = Console.ReadLine();
    var chosenplanPerformance = int.TryParse(input, out var chosenIndexPerf) ? chosenIndexPerf : -1;

    if (chosenplanPowersave == -1 || chosenplanPerformance == -1 || chosenplanPowersave > lines.Count || chosenplanPerformance > lines.Count)
    {
        Console.WriteLine("Please choose valid numbers for both choices.");
        await ChoosePowerPlan();
        return;
    }
    
    var powerSaveGuid = guidMatcher.Match(lines[chosenplanPowersave-1]).ToString();
    var performanceGuid = guidMatcher.Match(lines[chosenplanPerformance-1]).ToString();
    var plan = new PowerPlan{ PerformanceGuid = performanceGuid, PowerSaveGuid = powerSaveGuid};

    powerPlan = plan;
    await SavePowerPlanToFile(plan);
}

async Task SavePowerPlanToFile(PowerPlan plan)
{
    try
    {
        await File.WriteAllTextAsync(powerPlanPath, JsonSerializer.Serialize(plan));
        powerPlan = plan;
        Console.WriteLine("Successfully saved power plan choices");
    }
    catch (IOException)
    {
        logger.LogError("Could not write power plan to file, you will need to choose again next time."); 
    }
}

async Task<List<string>> GetWindowsPowerPlans()
{
    var list = new List<string>();
    var command = $"powercfg /list";
    var processStartInfo = new ProcessStartInfo("cmd", "/c " + command) {
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    try {
        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();
        var result = await process.StandardOutput.ReadToEndAsync();
        list = result.Split('\n').ToList();
        list = list.Where(l => l.StartsWith("GUID")).ToList();
        process.WaitForExit();
    }
    catch (Exception) {
        logger.LogError("Could not read available power plans, running in admin mode?");
    }

    return list;
}

_ = Task.Run(async() => {
    while (!cancellationSource.IsCancellationRequested) {
        var line = Console.ReadLine();
        if (String.IsNullOrWhiteSpace(line)) {
            await Task.Delay(100, cancellationSource.Token).ConfigureAwait(false);
            continue;
        }

        if (int.TryParse(line, out var coreOverride)) {
            Interlocked.Exchange(ref coresToPark, coreOverride);
            if (coreOverride >= numCores) {
                logger.LogError("You can't override more cores than you have.");
                continue;
            }
            
            logger.LogInformation("Future attempts at parking cores will park {coreOverride} cores.", coreOverride);
        }
    }
});

_ = Task.Run(async () => {
    bool realtime = false;
    while (!cancellationSource.IsCancellationRequested) {
        if (isLoading) {
            var proc = await GetPathOfExileProcess();
            if (!realtime) {
                if (proc is { Responding: false }) {
                    logger.LogWarning(
                        "PoE Process not Responding: Attempting to recover process by setting it to realtime. Note that you need to be running this program as an Administrator for this to work."
                    );
                    proc.PriorityClass = ProcessPriorityClass.RealTime;
                    realtime = true;
                }
            } else {
                if (proc == null || proc.HasExited) {
                    // PoE crashed or quit; reset realtime so we can do it next time PoE starts.
                    realtime = false;
                    isLoading = false;
                    logger.LogError("PoE quit while we set it realtime; resetting loading and realtime status.");
                }  
            }
        } else {
            if (realtime) {
                var proc = await GetPathOfExileProcess();
                if (proc != null) {
                    logger.LogInformation("Loading is done, falling back from realtime.");
                    proc.PriorityClass = ProcessPriorityClass.Normal;
                    realtime = false;
                }
            }
        }

        await Task.Delay(200, cancellationSource.Token).ConfigureAwait(false);
    }
});

// We'll use the PoE process to get the right client.txt path across standalone / Steam.
logger.LogInformation("Waiting for Path of Exile process to launch before doing anything.");

var proc = await GetPathOfExileProcess();
if (proc == null) {
    // If the game wasn't already running, it will log engine init before we start watching.
    // So if we started before the game starts, park first to prevent launch crashes.
    // We'll unpark when the user loads into a zone.
    proc = await WaitForExecutableToLaunch();
    await SetLowLoadMode();

   
}

string? gameDirectory = Path.GetDirectoryName(proc?.MainModule?.FileName);
if (string.IsNullOrEmpty(gameDirectory)) {
    logger.LogError("Couldn't detect game directory. Falling back to {fallbackGamePath}.", fallbackGamePath);
    gameDirectory = fallbackGamePath;
}

string clientTxtLocation = Path.Combine(gameDirectory, "logs", "client.txt");
string kakaoClientTxtLocation = Path.Combine(gameDirectory, "logs", "KakaoClient.txt");

string clientTxtPath = clientTxtLocation;
if (Path.Exists(kakaoClientTxtLocation)) {
    logger.LogInformation("Detected KR client log file -- using KakaoClient.txt as source instead.");
    clientTxtPath = kakaoClientTxtLocation;
}


await using var logStream = new FileStream(clientTxtPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
logStream.Seek(0, SeekOrigin.End);

using var reader = new StreamReader(logStream);

logger.LogInformation("Reading client data from {clientTxtPath}", clientTxtPath);



while (!cancellationSource.IsCancellationRequested) {
    var line = await reader.ReadLineAsync();
    if (String.IsNullOrWhiteSpace(line) || line.Length > 256) {
        await Task.Delay(20);
        continue;
    }

    if (startLoadMatcher.IsMatch(line) || startGameMatcher.IsMatch(line)) {
       await SetLowLoadMode();
    } else if (endLoadMatcher.IsMatch(line)) {
        await SetHighLoadMode();
    }
}


async Task<Process?> WaitForExecutableToLaunch() {
    while (!cancellationSource.IsCancellationRequested) {
        var poeProc = await GetPathOfExileProcess();
        if (poeProc is { HasExited: false }) {
            return poeProc;
        }
        
        await Task.Delay(200);
    }

    return null;
}

async Task SetLowLoadMode()
{
    if (ctdMode) await SetPowersavePlan();
    else await ParkCores();
}


async Task SetHighLoadMode()
{
    if (ctdMode) await SetPerformancePlan();
    else await ResumeCores();
}

async Task ParkCores() {
    var affinityBits = new StringBuilder(new string('1', numCores));
    for (int i = 0; i < coresToPark; i++) {
        affinityBits[affinityBits.Length - i - 1] = '0';
    }

    IntPtr affinity = new IntPtr(Convert.ToInt64(affinityBits.ToString(), 2));
    
    var poeProc = await GetPathOfExileProcess();
    if (poeProc is { HasExited: false }) {
        poeProc.ProcessorAffinity = affinity;
        logger.LogInformation("Parked cores: {affinityBits}", affinityBits);
        Interlocked.Exchange(ref isLoading, true);
    } else {
        logger.LogError("Detected loading screen, but could not find any process to park.");
    }
}

async Task SetPowersavePlan()
{
    var poeProc = await GetPathOfExileProcess();
     if (poeProc is { HasExited: false }) {
         logger.LogInformation("Setting powersave powerplan");
         await ChangePowerplan(powerPlan!.Value.PowerSaveGuid);
         Interlocked.Exchange(ref isLoading, true);
     } else {
        logger.LogError("Could not change powerplan, poe process was not found.");
     }
}

async Task SetPerformancePlan() {
    using var poeProc = await GetPathOfExileProcess();
    if (poeProc is { HasExited: false }) {
        logger.LogInformation("Setting performance powerplan");
        await ChangePowerplan(powerPlan!.Value.PerformanceGuid);
        Interlocked.Exchange(ref isLoading, false);
    } else {
        logger.LogError("Could not change powerplan, poe process was not found.");
    }
}

async Task ChangePowerplan(string planGuid)
{
    var command = $"powercfg /setactive {planGuid}";
    var processStartInfo = new ProcessStartInfo("cmd", "/c " + command) {
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    try {
        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();
        var result = await process.StandardOutput.ReadToEndAsync();
        process.WaitForExit();
        Console.WriteLine(result);
    }
    catch (Exception) {
         logger.LogError("Could not change powerplan, poe process was not found.");
    }
}

async Task ResumeCores() {
    var affinityBits = new StringBuilder(new string('1', numCores));

    IntPtr affinity = new IntPtr(Convert.ToInt64(affinityBits.ToString(), 2));
    
    var poeProc = await GetPathOfExileProcess();
    if (poeProc is { HasExited: false }) {
        poeProc.ProcessorAffinity = affinity;
        logger.LogInformation("Unparked cores: {affinityBits}", affinityBits);
        Interlocked.Exchange(ref isLoading, false);
    } else {
        logger.LogError("Detected loading screen, but could not find any process to unpark.");
    }
}

async Task<Process?> GetPathOfExileProcess() {
    // When PoE is first launched, it takes a bit of time for the MainWindowTitle to register.
    // The client is logging during this time though. So we can't find the process when needed.
    // We also want to do it as soon as possible to avoid giving it time to crash. 
    var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
    while (!cts.IsCancellationRequested) {
        var procs = Process.GetProcesses();
        Func<Process, bool> isPoE = (c =>
            c.MainWindowTitle.Equals("Path of Exile 2", StringComparison.InvariantCultureIgnoreCase)
            && c.ProcessName.Contains("PathOfExile")
        );
        var result = procs.FirstOrDefault(isPoE);
        if (result != null)
            return result;

        await Task.Delay(100);
    }

    return null;
}

int GetNumberProcessors() {
    // Some AMD CPUs will park half the cores for games.
    // This seems to make the number of threads that need to be passed in to the affinity not line up with the number of
    // threads that are reported by the `NUMBER_OF_PROCESSORS` environment variable.
    // So let's just try to calculate it by using our own process.
    var self = Process.GetCurrentProcess();
    
    // First, try the expected amount.
    try {
        self.ProcessorAffinity = new IntPtr(Convert.ToInt64(new string('1', Environment.ProcessorCount), 2));
        return Environment.ProcessorCount;
    } catch (Win32Exception) {
        logger.LogWarning(
            "Number of cores reported to us was {numCores}, but this appears different than the affinity. Trying to calculate real number.",
            Environment.ProcessorCount
        );
    }

    for (int i = Math.Min(64, Environment.ProcessorCount * 2); i > 0; i--) {
        try {
            self.ProcessorAffinity = new IntPtr(Convert.ToInt64(new string('1', i), 2));
            logger.LogWarning(
                "Calculated that we should be using {numCores} cores. Will proceed with that number.",
                i
            );
            return i;
        } catch(Win32Exception) {}
    }
    
    logger.LogCritical("Could not detect number of processors being used. We can't continue as a result. Please file a bug report.");
    throw new InvalidOperationException("Could not detect number of processors being used.");
}