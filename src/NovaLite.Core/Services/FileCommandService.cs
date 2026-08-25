using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NovaLite.Core.Services;

public sealed record KnownTerminalCommand(
    string CanonicalCommand,
    string Description,
    bool RequiresAdmin,
    string[] Aliases,
    string[] Keywords
);

public sealed class FileCommandService
{
    private bool _isEnabled = false;
    private string? _lastListedFolderPath = null; // Track the last folder that was listed for context-aware operations
    private bool _lastSelectionWasFolders = false; // Track when the last query was counting folders/directories
    private string? _pendingShellCommand = null; // Stored after the assistant suggests a terminal command
    private List<string> _pendingShellCommandOptions = new();

    // Confirmation state machine for system / fuzzy matched commands
    private string? _pendingConfirmationCommand = null;

    /// <summary>
    /// Whether the service is allowed to access local files and run commands. Default is false.
    /// </summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Enable or disable PC access for file commands and shell execution.
    /// </summary>
    public void SetEnabled(bool enabled) => _isEnabled = enabled;

    /// <summary>
    /// An optional workspace directory. When set, all executed commands run within this directory context.
    /// </summary>
    public string? WorkspaceDirectory { get; set; } = null;

    private static readonly KnownTerminalCommand[] KnownTerminalCommands = new[]
    {
        // ── System Repair & Health ──────────────────────────────────
        new KnownTerminalCommand(
            "bootrec /rebuildbcd",
            "Scans for Windows installations and rebuilds the Boot Configuration Data (BCD)",
            true,
            new[] { "rebuild bcd", "rebuild my bcd", "fix bcd", "repair bcd", "bootrec rebuildbcd", "bootrec /rebuildbcd", "rebuild boot configuration" },
            new[] { "rebuild bcd", "rebuildbcd", "bootrec" }
        ),
        new KnownTerminalCommand(
            "bootrec /fixmbr",
            "Writes a Windows-compatible Master Boot Record (MBR) to the system partition",
            true,
            new[] { "fix mbr", "fix my mbr", "repair mbr", "bootrec fixmbr", "bootrec /fixmbr", "fix master boot record" },
            new[] { "fix mbr", "fixmbr", "bootrec" }
        ),
        new KnownTerminalCommand(
            "sfc /scannow",
            "Scans and repairs corrupted Windows system files using System File Checker",
            true,
            new[] { "sfc scannow", "sfc scan now", "sfc /scan now", "sfc scanow", "sfcc scannow", "sfc scannw", "sfc scan", "system file checker", "scan system files", "repair system files", "fix system files", "check system files" },
            new[] { "sfc", "scannow", "system file checker" }
        ),
        new KnownTerminalCommand(
            "DISM /Online /Cleanup-Image /RestoreHealth",
            "Repairs corrupted Windows system image using Deployment Image Servicing and Management",
            true,
            new[] { "dism restore health", "dism restorehealth", "dism /online /cleanup-image /restorehealth", "dism cleanup image", "dism /online /cleanup image /restore health", "disrm restore health", "dism restore helath", "dism repair", "dism restore", "dism health", "deployment image servicing", "repair windows image", "fix windows image" },
            new[] { "dism", "restorehealth", "restore health", "cleanup-image" }
        ),
        new KnownTerminalCommand(
            "chkdsk C: /f",
            "Scans hard drive C: for file system errors and repairs them",
            true,
            new[] { "chkdsk", "check disk", "checkdisk", "chkdsk c:", "chkdsk /f", "chkdsk /r", "chkdsk c", "chekdisk", "check disk c", "scan disk", "scandisk", "disk check", "fix disk errors" },
            new[] { "chkdsk", "check disk", "scandisk" }
        ),
        new KnownTerminalCommand(
            "cleanmgr",
            "Opens the Disk Cleanup utility to free up space by removing temporary files",
            false,
            new[] { "disk cleanup", "clean disk", "cleanmgr", "disk cleaner", "clean up disk", "free up space", "free space", "clear temp files", "delete temp files", "remove temporary files", "disk clean" },
            new[] { "cleanmgr", "disk cleanup", "free space" }
        ),
        new KnownTerminalCommand(
            "defrag C: /O",
            "Optimizes and defragments the C: drive for better performance",
            true,
            new[] { "defrag", "defragment", "defrag c:", "defragment disk", "optimize drive", "defrag c", "defragement", "disk defragment", "optimize disk" },
            new[] { "defrag", "defragment", "optimize drive" }
        ),

        // ── Network & Connectivity ──────────────────────────────────
        new KnownTerminalCommand(
            "ipconfig /all",
            "Displays complete IP network configuration and adapter information",
            false,
            new[] { "ipconfig", "ip config", "ipconfg", "ipconfig all", "ipconfig /all", "my ip address", "show ip", "ip address", "my ip", "show my ip", "check ip", "network config", "network configuration", "ip info" },
            new[] { "ipconfig", "ip address", "my ip" }
        ),
        new KnownTerminalCommand(
            "ipconfig /flushdns",
            "Flushes and resets the DNS resolver cache",
            true,
            new[] { "flush dns", "flushdns", "ipconfig flushdns", "ipconfig /flushdns", "flush dns cache", "flus dns", "reset dns", "clear dns", "clear dns cache", "dns flush" },
            new[] { "flushdns", "flush dns" }
        ),
        new KnownTerminalCommand(
            "ipconfig /release",
            "Releases the current IP address obtained from DHCP",
            false,
            new[] { "ipconfig release", "ipconfig /release", "release ip", "release ip address", "drop ip" },
            new[] { "release ip" }
        ),
        new KnownTerminalCommand(
            "ipconfig /renew",
            "Requests a new IP address from DHCP server",
            false,
            new[] { "ipconfig renew", "ipconfig /renew", "renew ip", "renew ip address", "get new ip", "refresh ip" },
            new[] { "renew ip" }
        ),
        new KnownTerminalCommand(
            "ping google.com",
            "Pings Google servers to test internet connection and latency",
            false,
            new[] { "ping", "ping google", "ping google.com", "pingg google.com", "test connection", "ping 8.8.8.8", "ping internet", "test internet", "check internet", "internet test", "check connection", "am i connected", "test network" },
            new[] { "ping", "test internet", "check connection" }
        ),
        new KnownTerminalCommand(
            "tracert google.com",
            "Traces the network route to Google showing each hop and latency",
            false,
            new[] { "tracert", "traceroute", "trace route", "tracert google.com", "tracert google", "tracrt", "network route", "trace path", "traceroute google" },
            new[] { "tracert", "traceroute", "trace route" }
        ),
        new KnownTerminalCommand(
            "nslookup google.com",
            "Queries DNS server to resolve domain name to IP address",
            false,
            new[] { "nslookup", "dns lookup", "nslookup google.com", "nslookup google", "ns lookup", "resolve dns", "dns resolve", "lookup dns", "domain lookup" },
            new[] { "nslookup", "dns lookup" }
        ),
        new KnownTerminalCommand(
            "netstat -ano",
            "Lists active TCP/UDP connections and open ports with process IDs",
            false,
            new[] { "netstat", "net stat", "netstat ano", "netstat -ano", "open ports", "listening ports", "network connections", "nestat", "active connections", "show ports", "check ports" },
            new[] { "netstat", "open ports", "active connections" }
        ),
        new KnownTerminalCommand(
            "netsh wlan show profiles",
            "Displays saved Wi-Fi network profile names",
            false,
            new[] { "wifi passwords", "show wifi", "netsh wlan", "netsh wlan show profiles", "wifi profile", "show wifi profiles", "wifi names", "saved wifi", "wifi networks", "list wifi", "show saved wifi" },
            new[] { "wlan", "wifi", "wifi profiles" }
        ),
        new KnownTerminalCommand(
            "arp -a",
            "Displays the ARP cache showing IP-to-MAC address mappings on the network",
            false,
            new[] { "arp", "arp -a", "arp table", "arp cache", "mac addresses", "show mac", "network devices", "devices on network" },
            new[] { "arp", "mac address" }
        ),
        new KnownTerminalCommand(
            "route print",
            "Displays the IP routing table",
            false,
            new[] { "route print", "routing table", "show routes", "route table", "ip routes", "network routes" },
            new[] { "route", "routing table" }
        ),
        new KnownTerminalCommand(
            "getmac",
            "Displays the MAC addresses for all network adapters",
            false,
            new[] { "getmac", "get mac", "get mac address", "mac address", "show mac address", "my mac address", "find mac address", "physical address" },
            new[] { "getmac", "mac address" }
        ),
        new KnownTerminalCommand(
            "netsh advfirewall set allprofiles state off",
            "Disables Windows Firewall on all network profiles",
            true,
            new[] { "disable firewall", "turn off firewall", "firewall off", "stop firewall", "disable windows firewall" },
            new[] { "disable firewall" }
        ),
        new KnownTerminalCommand(
            "netsh advfirewall set allprofiles state on",
            "Enables Windows Firewall on all network profiles",
            true,
            new[] { "enable firewall", "turn on firewall", "firewall on", "start firewall", "enable windows firewall" },
            new[] { "enable firewall" }
        ),
        new KnownTerminalCommand(
            "netsh interface ip show config",
            "Shows detailed IP configuration for all network interfaces",
            false,
            new[] { "network interface config", "ip show config", "show network interfaces", "interface config", "network adapters config" },
            new[] { "interface config" }
        ),

        // ── System Information ──────────────────────────────────────
        new KnownTerminalCommand(
            "systeminfo",
            "Displays detailed Windows OS version and system hardware specifications",
            false,
            new[] { "systeminfo", "system info", "sysinfo", "systeminfo.exe", "pc info", "specs", "system specs", "systm info", "computer info", "my pc info", "pc specs", "computer specs", "hardware info", "os info", "windows info", "system information" },
            new[] { "systeminfo", "sysinfo", "pc info", "specs" }
        ),
        new KnownTerminalCommand(
            "hostname",
            "Displays the name of your computer on the network",
            false,
            new[] { "hostname", "host name", "computer name", "pc name", "my computer name", "my pc name", "device name", "machine name", "what is my computer name", "whats my pc name" },
            new[] { "hostname", "computer name", "pc name" }
        ),
        new KnownTerminalCommand(
            "whoami",
            "Displays the current logged-in user account name",
            false,
            new[] { "whoami", "who am i", "current user", "my username", "logged in user", "what user am i", "my user", "my account", "username" },
            new[] { "whoami", "current user", "username" }
        ),
        new KnownTerminalCommand(
            "ver",
            "Displays the Windows version number",
            false,
            new[] { "ver", "windows version", "win version", "os version", "what version of windows", "which windows", "my windows version", "check windows version" },
            new[] { "ver", "windows version" }
        ),
        new KnownTerminalCommand(
            "wmic os get caption,version,buildnumber",
            "Shows the Windows edition, version, and build number",
            false,
            new[] { "windows edition", "windows build", "os build", "build number", "windows build number", "os edition", "which edition of windows" },
            new[] { "windows edition", "build number" }
        ),
        new KnownTerminalCommand(
            "wmic cpu get name,numberofcores,maxclockspeed",
            "Shows CPU processor name, core count, and max clock speed",
            false,
            new[] { "cpu info", "processor info", "what cpu", "my cpu", "my processor", "cpu name", "cpu cores", "cpu speed", "what processor", "show cpu", "check cpu" },
            new[] { "cpu info", "processor" }
        ),
        new KnownTerminalCommand(
            "wmic memorychip get capacity,speed,manufacturer",
            "Shows RAM module capacity, speed, and manufacturer",
            false,
            new[] { "ram info", "memory info", "how much ram", "my ram", "ram size", "ram speed", "memory size", "check ram", "show ram", "total ram", "how much memory" },
            new[] { "ram info", "memory info" }
        ),
        new KnownTerminalCommand(
            "wmic diskdrive get model,size,status",
            "Shows hard drive model, capacity, and health status",
            false,
            new[] { "disk info", "hard drive info", "hdd info", "ssd info", "drive info", "storage info", "disk size", "hard drive size", "how much storage", "check storage", "my hard drive", "disk model" },
            new[] { "disk info", "hard drive", "storage info" }
        ),
        new KnownTerminalCommand(
            "wmic bios get serialnumber",
            "Displays the computer's BIOS serial number",
            false,
            new[] { "serial number", "bios serial", "pc serial number", "computer serial", "my serial number", "device serial", "hardware serial" },
            new[] { "serial number", "bios serial" }
        ),
        new KnownTerminalCommand(
            "wmic path win32_VideoController get name,driverversion,adapterram",
            "Shows GPU/graphics card name, driver version, and video memory",
            false,
            new[] { "gpu info", "graphics card info", "video card info", "my gpu", "what gpu", "graphics info", "gpu name", "gpu driver", "check gpu", "show gpu", "video adapter", "display adapter" },
            new[] { "gpu info", "graphics card" }
        ),

        // ── Process & Task Management ───────────────────────────────
        new KnownTerminalCommand(
            "tasklist",
            "Lists all currently active running system processes",
            false,
            new[] { "tasklist", "task list", "running processes", "list processes", "show processes", "tasklsit", "get processes", "see processes", "active processes", "all processes", "process list" },
            new[] { "tasklist", "processes", "running processes" }
        ),
        new KnownTerminalCommand(
            "taskkill /IM notepad.exe /F",
            "Force closes a running program by its name (example: Notepad)",
            false,
            new[] { "kill process", "end process", "force close", "taskkill", "task kill", "kill task", "stop process", "close program", "force quit", "end task" },
            new[] { "taskkill", "kill process", "end task" }
        ),

        // ── Power & Shutdown ────────────────────────────────────────
        new KnownTerminalCommand(
            "shutdown /r /t 60",
            "Schedules a computer restart in 60 seconds",
            true,
            new[] { "restart pc", "restart computer", "reboot pc", "reboot computer", "restart windows", "reboot", "restart my pc", "restart my computer" },
            new[] { "restart", "reboot" }
        ),
        new KnownTerminalCommand(
            "shutdown /s /t 60",
            "Schedules a computer shutdown in 60 seconds",
            true,
            new[] { "shutdown pc", "shutdown computer", "turn off pc", "turn off computer", "power off", "shut down", "shutdown my pc", "shutdown my computer", "turn off my pc" },
            new[] { "shutdown", "turn off", "power off" }
        ),
        new KnownTerminalCommand(
            "shutdown /a",
            "Cancels a pending shutdown or restart",
            true,
            new[] { "cancel shutdown", "abort shutdown", "stop shutdown", "cancel restart", "abort restart", "stop restart", "cancel reboot", "dont shutdown", "dont restart" },
            new[] { "cancel shutdown", "abort shutdown" }
        ),
        new KnownTerminalCommand(
            "shutdown /l",
            "Logs off the current user session immediately",
            false,
            new[] { "log off", "logoff", "sign out", "log out", "logout", "sign off" },
            new[] { "log off", "sign out" }
        ),
        new KnownTerminalCommand(
            "rundll32.exe user32.dll,LockWorkStation",
            "Locks the computer screen immediately",
            false,
            new[] { "lock pc", "lock computer", "lock screen", "lock my pc", "lock my computer", "lock workstation", "lock my screen" },
            new[] { "lock pc", "lock screen", "lock computer" }
        ),
        new KnownTerminalCommand(
            "powercfg /batteryreport",
            "Generates a detailed battery health and usage report (saved as HTML)",
            true,
            new[] { "battery report", "battery health", "battery status", "check battery", "battery info", "powercfg battery", "battery life", "how is my battery", "battery condition" },
            new[] { "battery report", "battery health" }
        ),
        new KnownTerminalCommand(
            "powercfg /energy",
            "Runs a 60-second energy analysis and generates an efficiency report",
            true,
            new[] { "energy report", "power report", "energy analysis", "power analysis", "power efficiency", "energy efficiency", "powercfg energy" },
            new[] { "energy report", "power report" }
        ),

        // ── Group Policy & Updates ──────────────────────────────────
        new KnownTerminalCommand(
            "gpupdate /force",
            "Forces an immediate refresh of local and domain Group Policy settings",
            true,
            new[] { "gpupdate", "gpupdate force", "gp update", "group policy update", "update group policy", "gpudpate", "gpupdate /force", "refresh group policy", "force group policy" },
            new[] { "gpupdate", "group policy" }
        ),

        // ── User & Account Management ───────────────────────────────
        new KnownTerminalCommand(
            "net user",
            "Lists all user accounts on this computer",
            false,
            new[] { "net user", "list users", "show users", "user accounts", "all users", "who has accounts", "local users", "user list" },
            new[] { "net user", "user accounts", "list users" }
        ),
        new KnownTerminalCommand(
            "net localgroup administrators",
            "Shows all members of the Administrators group",
            false,
            new[] { "admin users", "administrator accounts", "who is admin", "admin group", "local admins", "net localgroup administrators", "list admins", "show administrators" },
            new[] { "admin users", "administrators" }
        ),

        // ── Service Management ──────────────────────────────────────
        new KnownTerminalCommand(
            "net start",
            "Lists all currently running Windows services",
            false,
            new[] { "net start", "running services", "list services", "show services", "active services", "started services", "windows services" },
            new[] { "net start", "running services", "list services" }
        ),
        new KnownTerminalCommand(
            "sc query",
            "Queries the status of all Windows services",
            false,
            new[] { "sc query", "service status", "query services", "check services", "service query", "all services status" },
            new[] { "sc query", "service status" }
        ),

        // ── Disk & Storage ──────────────────────────────────────────
        new KnownTerminalCommand(
            "wmic logicaldisk get name,size,freespace,filesystem",
            "Shows all drives with total size, free space, and file system type",
            false,
            new[] { "disk space", "free space", "drive space", "how much space", "storage space", "check disk space", "available space", "remaining space", "drive size", "c drive space", "disk usage", "how full is my disk", "space left" },
            new[] { "disk space", "free space", "drive space" }
        ),

        // ── Windows Tools & Utilities ───────────────────────────────
        new KnownTerminalCommand(
            "msinfo32",
            "Opens the detailed System Information GUI window",
            false,
            new[] { "msinfo32", "msinfo", "system information window", "detailed system info", "open system info", "system info gui", "advanced system info" },
            new[] { "msinfo32" }
        ),
        new KnownTerminalCommand(
            "devmgmt.msc",
            "Opens Device Manager to view and manage hardware devices",
            false,
            new[] { "device manager", "devmgmt", "devmgmt.msc", "open device manager", "hardware manager", "manage devices", "show devices", "my devices" },
            new[] { "device manager" }
        ),
        new KnownTerminalCommand(
            "diskmgmt.msc",
            "Opens Disk Management to view and manage disk partitions",
            false,
            new[] { "disk management", "diskmgmt", "diskmgmt.msc", "open disk management", "manage disks", "partition manager", "disk partitions", "manage partitions" },
            new[] { "disk management" }
        ),
        new KnownTerminalCommand(
            "services.msc",
            "Opens the Services management console",
            false,
            new[] { "services.msc", "open services", "services console", "windows services manager", "manage services", "service manager" },
            new[] { "services.msc", "services console" }
        ),
        new KnownTerminalCommand(
            "taskmgr",
            "Opens the Windows Task Manager",
            false,
            new[] { "task manager", "taskmgr", "open task manager", "taskmanager", "tsk manager", "task manger", "performance monitor", "show task manager" },
            new[] { "task manager", "taskmgr" }
        ),
        new KnownTerminalCommand(
            "control",
            "Opens the Windows Control Panel",
            false,
            new[] { "control panel", "control", "open control panel", "control pannel", "contrl panel", "controlpanel" },
            new[] { "control panel" }
        ),
        new KnownTerminalCommand(
            "ms-settings:",
            "Opens the Windows Settings app",
            false,
            new[] { "settings", "windows settings", "open settings", "pc settings", "system settings", "my settings", "computer settings" },
            new[] { "settings", "windows settings" }
        ),
        new KnownTerminalCommand(
            "resmon",
            "Opens the Resource Monitor for detailed CPU, memory, disk, and network usage",
            false,
            new[] { "resource monitor", "resmon", "open resource monitor", "resource manager", "detailed performance", "system resources" },
            new[] { "resource monitor", "resmon" }
        ),
        new KnownTerminalCommand(
            "eventvwr",
            "Opens the Windows Event Viewer to browse system and application logs",
            false,
            new[] { "event viewer", "eventvwr", "event log", "event logs", "system log", "system logs", "windows logs", "open event viewer", "view events", "check logs" },
            new[] { "event viewer", "event log" }
        ),
        new KnownTerminalCommand(
            "compmgmt.msc",
            "Opens Computer Management console (disks, services, events, users)",
            false,
            new[] { "computer management", "compmgmt", "compmgmt.msc", "open computer management", "manage computer" },
            new[] { "computer management" }
        ),
        new KnownTerminalCommand(
            "dxdiag",
            "Opens the DirectX Diagnostic Tool showing display, sound, and input info",
            false,
            new[] { "dxdiag", "directx diagnostic", "directx info", "direct x diagnostic", "dx diag", "directx", "display diagnostic", "graphics diagnostic" },
            new[] { "dxdiag", "directx" }
        ),
        new KnownTerminalCommand(
            "winver",
            "Shows the About Windows dialog with edition and build info",
            false,
            new[] { "winver", "about windows", "windows about", "windows build info", "windows edition info", "win ver" },
            new[] { "winver", "about windows" }
        ),
        new KnownTerminalCommand(
            "msconfig",
            "Opens System Configuration to manage startup and boot settings",
            false,
            new[] { "msconfig", "system configuration", "system config", "startup config", "boot config", "open msconfig", "boot options" },
            new[] { "msconfig", "system configuration" }
        ),
        new KnownTerminalCommand(
            "regedit",
            "Opens the Windows Registry Editor",
            true,
            new[] { "regedit", "registry editor", "registry", "open registry", "edit registry", "windows registry", "reg edit" },
            new[] { "regedit", "registry" }
        ),
        new KnownTerminalCommand(
            "mstsc",
            "Opens Remote Desktop Connection client",
            false,
            new[] { "remote desktop", "mstsc", "rdp", "remote connection", "open remote desktop", "connect remote", "remote desktop connection" },
            new[] { "remote desktop", "rdp", "mstsc" }
        ),

        // ── Common Apps ─────────────────────────────────────────────
        new KnownTerminalCommand(
            "calc",
            "Opens the Windows Calculator",
            false,
            new[] { "calculator", "calc", "open calculator", "open calc", "calulator", "calculater" },
            new[] { "calculator", "calc" }
        ),
        new KnownTerminalCommand(
            "notepad",
            "Opens Notepad text editor",
            false,
            new[] { "notepad", "open notepad", "text editor", "note pad", "notpad", "open text editor" },
            new[] { "notepad" }
        ),
        new KnownTerminalCommand(
            "mspaint",
            "Opens Microsoft Paint drawing app",
            false,
            new[] { "paint", "mspaint", "open paint", "ms paint", "microsoft paint", "drawing app" },
            new[] { "paint", "mspaint" }
        ),
        new KnownTerminalCommand(
            "snippingtool",
            "Opens the Snipping Tool for taking screenshots",
            false,
            new[] { "snipping tool", "snippingtool", "screenshot tool", "screen capture", "take screenshot", "snip tool", "screen snip", "print screen tool" },
            new[] { "snipping tool", "screenshot" }
        ),
        new KnownTerminalCommand(
            "explorer",
            "Opens File Explorer",
            false,
            new[] { "file explorer", "explorer", "open explorer", "open file explorer", "my computer", "this pc", "open this pc", "open my computer", "files", "open files" },
            new[] { "file explorer", "explorer" }
        ),
        new KnownTerminalCommand(
            "cmd",
            "Opens a new Command Prompt window",
            false,
            new[] { "command prompt", "cmd", "open cmd", "open command prompt", "terminal", "open terminal", "comand prompt", "commnd prompt" },
            new[] { "command prompt", "cmd" }
        ),
        new KnownTerminalCommand(
            "powershell",
            "Opens a Windows PowerShell window",
            false,
            // "PS" is ambiguous in conversation (for example, PlayStation), so
            // require an explicit PowerShell reference.
            new[] { "powershell", "open powershell", "power shell", "pwsh", "powrshell" },
            new[] { "powershell" }
        ),

        // ── Network Sharing & Drives ────────────────────────────────
        new KnownTerminalCommand(
            "net share",
            "Lists all shared folders on this computer",
            false,
            new[] { "shared folders", "net share", "network shares", "list shares", "show shares", "shared resources" },
            new[] { "net share", "shared folders" }
        ),
        new KnownTerminalCommand(
            "net use",
            "Lists mapped network drives and connections",
            false,
            new[] { "mapped drives", "net use", "network drives", "show mapped drives", "list network drives", "connected drives" },
            new[] { "net use", "mapped drives" }
        ),

        // ── File System Utilities ───────────────────────────────────
        new KnownTerminalCommand(
            "attrib",
            "Displays or changes file attributes (hidden, read-only, system, archive)",
            false,
            new[] { "attrib", "file attributes", "show file attributes", "change file attributes", "hidden files", "unhide files", "show hidden files" },
            new[] { "attrib", "file attributes" }
        ),
        new KnownTerminalCommand(
            "cipher /w:C:\\",
            "Securely wipes deleted data from free space on the C: drive",
            true,
            new[] { "cipher wipe", "wipe free space", "secure delete", "cipher", "wipe disk", "overwrite free space", "secure wipe" },
            new[] { "cipher", "wipe free space" }
        ),
        new KnownTerminalCommand(
            "assoc",
            "Displays or modifies file extension associations",
            false,
            new[] { "assoc", "file associations", "file extension associations", "default programs", "change file association" },
            new[] { "assoc", "file associations" }
        ),

        // ── Clipboard & Display ─────────────────────────────────────
        new KnownTerminalCommand(
            "echo %PATH%",
            "Displays the system PATH environment variable",
            false,
            new[] { "show path", "echo path", "system path", "path variable", "environment path", "my path", "check path" },
            new[] { "path", "system path" }
        ),
        new KnownTerminalCommand(
            "set",
            "Displays all environment variables currently set",
            false,
            new[] { "environment variables", "env variables", "show environment", "list env", "set command", "all variables" },
            new[] { "environment variables" }
        ),

        // ── Recycle Bin ─────────────────────────────────────────────
        new KnownTerminalCommand(
            "rd /s /q C:\\$Recycle.Bin",
            "Empties the Recycle Bin by deleting all items permanently",
            true,
            new[] { "empty recycle bin", "clear recycle bin", "delete recycle bin", "empty trash", "clear trash", "empty the recycle bin", "clean recycle bin", "recycle bin empty" },
            new[] { "empty recycle bin", "clear recycle bin", "empty trash" }
        )
    };

    private static readonly IReadOnlyDictionary<string, string[]> DownloadCategories = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Documents"] = new[] { ".doc", ".docx", ".pdf", ".txt", ".md", ".csv", ".rtf", ".xls", ".xlsx", ".ppt", ".pptx" },
        ["Images"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" },
        ["Audio"] = new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac" },
        ["Video"] = new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv" },
        ["Archives"] = new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" },
        ["Installers"] = new[] { ".exe", ".msi" },
        ["Code"] = new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".h", ".rs", ".go", ".php", ".html", ".css", ".json", ".xml", ".yaml", ".yml" }
    };

    private readonly string _downloadsFolder;

    public FileCommandService(string? downloadsFolder = null)
    {
        _downloadsFolder = !string.IsNullOrWhiteSpace(downloadsFolder)
            ? downloadsFolder
            : GetDefaultDownloadsFolder();
    }

    public async Task<(bool Handled, string Response)> TryHandleCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return (false, string.Empty);

        var normalized = command.Trim();
        var lower = normalized.ToLowerInvariant();

        // 1. Pending confirmation response check (User confirming or cancelling a system/fuzzy command)
        if (_pendingConfirmationCommand != null)
        {
            if (IsPositiveConfirmation(lower))
            {
                var cmdToRun = _pendingConfirmationCommand;
                _pendingConfirmationCommand = null;
                if (!IsEnabled)
                    return (true, "PC access is disabled. Turn on the 'PC Access' toggle in the app to allow Nova to access your files and run commands.");

                var result = await ExecuteShellCommandAsync(cmdToRun);
                return (true, $"⚡ Executing command: `{cmdToRun}`...\n\n{result}");
            }

            if (IsNegativeConfirmation(lower))
            {
                _pendingConfirmationCommand = null;
                return (true, "❌ Command execution cancelled.");
            }
        }

        // 2. Check Google-like Fuzzy Command & System Intent Recognizer (e.g. "run the sfc scannow command", "disrm restore health", "ipconfg")
        if (TryMatchFuzzyKnownCommand(lower, out var matchedCommand, out var isExactDirectRun))
        {
            if (!IsEnabled)
                return (true, "PC access is disabled. Turn on the 'PC Access' toggle in the app to allow Nova to access your files and run commands.");

            if (isExactDirectRun)
            {
                return (true, await ExecuteShellCommandAsync(matchedCommand.CanonicalCommand));
            }

            _pendingConfirmationCommand = matchedCommand.CanonicalCommand;
            var adminLabel = matchedCommand.RequiresAdmin ? " *(Requires Administrator Privileges)*" : string.Empty;
            var confirmationMsg = $"❓ Did you mean: **`{matchedCommand.CanonicalCommand}`**?{adminLabel}\n\n" +
                                  $"*{matchedCommand.Description}*\n\n" +
                                  $"Reply **yes**, **run**, or **confirm** to execute this command.";
            return (true, confirmationMsg);
        }

        // Check if command is a file/PC command
        var isFileCmd = IsOrganizeDownloadsCommand(lower) || TryMatchOrganizeSpecificFolderCommand(lower) || TryMatchListFilesByTypeAndLocation(lower, out _, out _) || IsListDownloadsCommand(lower) || IsCountWordDocumentsCommand(lower) || TryMatchFindFileCommand(lower, out _) || TryMatchListLocationCommand(lower, out _) || TryMatchCountCommand(lower, out _, out _) || TryMatchCreateFolderAndMoveFilesCommand(lower, out _, out _, out _) || TryMatchCreateFolderCommand(lower, out _, out _) || TryMatchCreateFileCommand(lower, out _, out _) || TryMatchPerformOptionCommand(lower, out _) || TryMatchRunPreviousShellCommand(lower, out _) || TryMatchShellCommand(lower, out _) || TryMatchExistenceCommand(lower, out _, out _) || TryMatchListSpecificFolderCommand(lower, out _) || TryMatchListLastResultCommand(lower) || TryMatchOpenFileCommand(lower, out _) || TryMatchDeleteCommand(lower, out _) || TryMatchRenameCommand(lower, out _, out _) || TryMatchMoveOrCopyCommand(lower, out _, out _, out _);
        if (isFileCmd && !IsEnabled)
            return (true, "PC access is disabled. Turn on the 'PC Access' toggle in the app to allow Nova to access your files.");

        // Create a new folder and move matching files into it
        if (TryMatchCreateFolderAndMoveFilesCommand(lower, out var newFolderName, out var requestedFileType, out var folderLocation))
        {
            var path = ResolveCreateFolderLocation(folderLocation);
            if (string.IsNullOrEmpty(path))
                return (true, "I couldn't resolve which folder to create the new folder in. Please say something like \"create a folder called 123 on my desktop\".");

            return (true, await CreateFolderAndMoveFilesAsync(path, newFolderName, requestedFileType));
        }

        // Create a folder only (e.g. "create a folder on my desktop named 234", "create a folder namd 234 on my desktop", "I need you to create a folder on my desktop and name it 234")
        if (TryMatchCreateFolderCommand(lower, out var createFolderName, out var createFolderLocation))
        {
            var path = ResolveCreateFolderLocation(createFolderLocation);
            if (string.IsNullOrEmpty(path))
                path = GetDefaultDesktopFolder();

            return (true, await CreateFolderOnlyAsync(path, createFolderName));
        }

        // Create a text or data file (e.g. "create a file test.txt on desktop", "make a text file 234.txt")
        if (TryMatchCreateFileCommand(lower, out var createFileName, out var createFileLocation))
        {
            var path = ResolveCreateFolderLocation(createFileLocation);
            if (string.IsNullOrEmpty(path))
                path = GetDefaultDesktopFolder();

            return (true, await CreateFileOnlyAsync(path, createFileName));
        }

        // Organize specific folder
        if (TryMatchOrganizeSpecificFolderCommand(lower))
        {
            if (!string.IsNullOrEmpty(_lastListedFolderPath))
                return (true, await OrganizeAsync(_lastListedFolderPath));
            else
                return (true, "Please list a folder first, then I can organize it for you.");
        }

        // Basic PC Operations (Delete, Rename, Move, Copy)
        if (TryMatchDeleteCommand(lower, out var deleteTarget))
        {
            return (true, await DeleteItemAsync(deleteTarget));
        }

        if (TryMatchRenameCommand(lower, out var renameOld, out var renameNew))
        {
            return (true, await RenameItemAsync(renameOld, renameNew));
        }

        if (TryMatchMoveOrCopyCommand(lower, out var isCopy, out var sourceItem, out var destFolder))
        {
            var destPath = ResolveKnownFolder(destFolder) ?? GetDefaultDesktopFolder();
            return (true, await MoveOrCopyItemAsync(sourceItem, destPath, isCopy));
        }

        // Organize root downloads folder
        if (IsOrganizeDownloadsCommand(lower))
            return (true, await OrganizeAsync(_downloadsFolder));

        // List files by type and location (e.g. "list all the word documents on my desktop")
        if (TryMatchListFilesByTypeAndLocation(lower, out var fileType, out var fileLoc))
        {
            var path = ResolveKnownFolder(fileLoc ?? "desktop");
            if (string.IsNullOrEmpty(path)) return (true, "I couldn't resolve that folder.");
            _lastListedFolderPath = path;
            return (true, await ListFilesByTypeInLocationAsync(path, fileType));
        }

        // List downloads or other known location
        string? listLoc = null;
        if (TryMatchListLocationCommand(lower, out var tmpLoc)) listLoc = tmpLoc;
        if (IsListDownloadsCommand(lower) || listLoc != null)
        {
            var path = ResolveKnownFolder(listLoc ?? (IsListDownloadsCommand(lower) ? "downloads" : "desktop"));
            if (string.IsNullOrEmpty(path)) return (true, "I couldn't resolve that folder.");
            _lastListedFolderPath = path;
            _lastSelectionWasFolders = false;
            return (true, await ListLocationAsync(path));
        }

        // Count queries
        string? countType = null; string? countLoc = null;
        if (TryMatchCountCommand(lower, out var tmpType, out var tmpCountLoc)) { countType = tmpType; countLoc = tmpCountLoc; }

        if (countType == null && Regex.IsMatch(lower, @"\bhow many\b.*\b(?:pdf|pdfs|pdf files)\b.*\b(?:in|on|from|at|inside|within)\b.*\b(?:documents?|documents folder)\b"))
        {
            countType = "pdf";
            countLoc = "documents";
        }

        if (IsCountWordDocumentsCommand(lower) || countType != null)
        {
            string? path;
            if (countLoc != null)
                path = ResolveCountLocation(countLoc);
            else if (Regex.IsMatch(lower, @"\b(?:here|this folder|this directory|current folder|current directory|this location|this path)\b"))
                path = !string.IsNullOrEmpty(_lastListedFolderPath) ? _lastListedFolderPath : GetDefaultDesktopFolder();
            else
                path = ResolveKnownFolder("desktop");

            if (string.IsNullOrEmpty(path)) return (true, "I couldn't resolve that folder.");
            _lastListedFolderPath = path;
            _lastSelectionWasFolders = countType != null && (countType.Contains("folder") || countType.Contains("folders") || countType.Contains("directory") || countType.Contains("directories") || countType.Contains("subfolder") || countType.Contains("subfolders"));
            return (true, await CountFilesInLocationAsync(path, countType ?? "word"));
        }

        // Existence checks
        string? existType = null; string? existLoc = null;
        if (TryMatchExistenceCommand(lower, out var tmpExistType, out var tmpExistLoc)) { existType = tmpExistType; existLoc = tmpExistLoc; }
        if (existType != null)
        {
            var path = ResolveKnownFolder(existLoc ?? "desktop");
            if (string.IsNullOrEmpty(path)) return (true, "I couldn't resolve that folder.");
            return (true, await ExistsInLocationAsync(path, existType));
        }

        // Follow-up list requests like "list them" or "show those"
        if (TryMatchListLastResultCommand(lower))
        {
            if (string.IsNullOrEmpty(_lastListedFolderPath))
                return (true, "Please ask about a folder or location first, and then I can list it.");

            if (_lastSelectionWasFolders)
                return (true, await ListDirectoriesInLocationAsync(_lastListedFolderPath));

            return (true, await ListLocationAsync(_lastListedFolderPath));
        }

        // List specific subfolder
        if (TryMatchListSpecificFolderCommand(lower, out var folderName))
        {
            var folderPath = FindSubfolderInDownloads(folderName);
            if (!string.IsNullOrEmpty(folderPath))
            {
                _lastListedFolderPath = folderPath;
                _lastSelectionWasFolders = false;
                return (true, await ListLocationAsync(folderPath));
            }
        }

        // Follow-up execute a numbered option from the last assistant response
        if (TryMatchPerformOptionCommand(lower, out var optionIndex))
        {
            if (_pendingShellCommandOptions.Count == 0)
                return (true, "I don't have any numbered commands from the last assistant response to execute.");

            if (optionIndex < 1 || optionIndex > _pendingShellCommandOptions.Count)
                return (true, $"I only have {_pendingShellCommandOptions.Count} numbered command{(_pendingShellCommandOptions.Count == 1 ? string.Empty : "s")} available.");

            var cmd = _pendingShellCommandOptions[optionIndex - 1];
            return (true, await ExecuteShellCommandAsync(cmd));
        }

        // Follow-up execute command requests like "okay run it" or "execute that"
        if (TryMatchRunPreviousShellCommand(lower, out var pendingShellCommand))
        {
            return (true, await ExecuteShellCommandAsync(pendingShellCommand));
        }

        // Direct explicit shell command execution requests
        if (TryMatchShellCommand(lower, out var shellCommand))
        {
            return (true, await ExecuteShellCommandAsync(shellCommand));
        }

        // Open file
        if (TryMatchOpenFileCommand(lower, out var fileToOpen))
        {
            return (true, await OpenFileAsync(fileToOpen));
        }

        // Find file
        if (TryMatchFindFileCommand(lower, out var searchTerm))
            return (true, await FindFileAsync(searchTerm));

        return (false, string.Empty);
    }

    private static bool IsPositiveConfirmation(string lower)
    {
        return Regex.IsMatch(lower, @"^\s*(?:yes|y|yeah|yep|sure|ok|okay|confirm|run|do\s+it|execute|go\s+ahead|run\s+it|please|yes\s+run\s+it|do\s+that|1)\s*$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(lower, @"\b(?:yes|confirm|run\s+it|go\s+ahead|do\s+it)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsNegativeConfirmation(string lower)
    {
        return Regex.IsMatch(lower, @"^\s*(?:no|nope|cancel|stop|dont|don't|nevermind|forget\s+it)\s*$", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(lower, @"\b(?:don't\s+run|cancel|stop)\b", RegexOptions.IgnoreCase);
    }

    private static bool TryMatchFuzzyKnownCommand(string input, out KnownTerminalCommand match, out bool isExactDirectRun)
    {
        match = default!;
        isExactDirectRun = false;

        if (string.IsNullOrWhiteSpace(input)) return false;

        // Skip fuzzy matching for long texts or multi-line code pastes
        if (input.Length > 200 || input.Contains('\n')) return false;

        var cleaned = input.Trim();
        var lower = cleaned.ToLowerInvariant();

        // This recognizer controls the user's PC. Informational questions belong to
        // the model, even if they mention a command or application by name.
        if (Regex.IsMatch(lower, @"^\s*(?:what|why|how|when|where|who)\b|^\s*(?:tell me|explain|describe|compare|recommend)\b|^\s*can\s+(?:i|you)\s+(?:explain|tell|show|describe)\b", RegexOptions.IgnoreCase))
            return false;

        // Strip filler words
        var stripped = Regex.Replace(lower, @"^\b(?:run\s+the|execute\s+the|run|execute|can\s+you\s+run|please\s+run|do|launch)\s+", string.Empty);
        stripped = Regex.Replace(stripped, @"\s+(?:command|cmd|utility|tool|process)$", string.Empty).Trim();

        // 1. Direct match on CanonicalCommand
        foreach (var cmd in KnownTerminalCommands)
        {
            if (string.Equals(lower, cmd.CanonicalCommand, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stripped, cmd.CanonicalCommand, StringComparison.OrdinalIgnoreCase))
            {
                match = cmd;
                isExactDirectRun = false;
                return true;
            }
        }

        // 2. Exact match on Aliases
        foreach (var cmd in KnownTerminalCommands)
        {
            foreach (var alias in cmd.Aliases)
            {
                if (string.Equals(lower, alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(stripped, alias, StringComparison.OrdinalIgnoreCase))
                {
                    match = cmd;
                    return true;
                }
            }
        }

        // 3. Substring match for phrases like "run the sfc scannow command" or "run dism restore health command"
        foreach (var cmd in KnownTerminalCommands)
        {
            foreach (var alias in cmd.Aliases)
            {
                var pattern = $@"\b{Regex.Escape(alias)}\b";
                if (Regex.IsMatch(lower, pattern, RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(stripped, pattern, RegexOptions.IgnoreCase))
                {
                    match = cmd;
                    return true;
                }
            }
        }

        // 4. Fuzzy Levenshtein Distance match for typos (e.g. sfc scanow, disrm restore health, ipconfg)
        KnownTerminalCommand? bestMatch = null;
        int minDistance = int.MaxValue;

        var strippedTokens = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var cmd in KnownTerminalCommands)
        {
            foreach (var alias in cmd.Aliases)
            {
                var lowerAlias = alias.ToLowerInvariant();
                int dist = CalculateLevenshteinDistance(stripped, lowerAlias);
                
                // For very short aliases or inputs, require exact match or a much stricter threshold
                int threshold = Math.Max(1, lowerAlias.Length / 4);
                if (lowerAlias.Length <= 3 || stripped.Length <= 3)
                {
                    threshold = 0; // Exact match only for short strings
                }

                if (dist <= threshold && dist < minDistance)
                {
                    minDistance = dist;
                    bestMatch = cmd;
                }

                var aliasTokens = lowerAlias.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (strippedTokens.Length == aliasTokens.Length && strippedTokens.Length > 0)
                {
                    int tokenDistSum = 0;
                    bool tokensMatchFuzzily = true;
                    for (int i = 0; i < strippedTokens.Length; i++)
                    {
                        int tDist = CalculateLevenshteinDistance(strippedTokens[i], aliasTokens[i]);
                        int tThreshold = Math.Max(1, aliasTokens[i].Length / 3);
                        if (tDist > tThreshold)
                        {
                            tokensMatchFuzzily = false;
                            break;
                        }
                        tokenDistSum += tDist;
                    }

                    if (tokensMatchFuzzily && tokenDistSum < minDistance)
                    {
                        minDistance = tokenDistSum;
                        bestMatch = cmd;
                    }
                }
            }
        }

        if (bestMatch != null)
        {
            match = bestMatch;
            return true;
        }

        return false;
    }

    private static int CalculateLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int[,] d = new int[s.Length + 1, t.Length + 1];
        for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[s.Length, t.Length];
    }

    private static bool IsOrganizeDownloadsCommand(string lower)
    {
        return Regex.IsMatch(lower, @"\b(organize|tidy|clean\s*up|sort)\b.*\bdownloads?\b") ||
               Regex.IsMatch(lower, @"\bdownloads?\b.*\b(organize|tidy|clean\s*up|sort)\b");
    }

    private static bool TryMatchOrganizeSpecificFolderCommand(string lower)
    {
        return Regex.IsMatch(lower, @"\b(organize|tidy|clean\s*up|sort|put)\b.*\b(this|that|these|them)\b") ||
               Regex.IsMatch(lower, @"\b(this|that|these|them)\b.*\b(organize|tidy|clean\s*up|sort|put)\b") ||
               Regex.IsMatch(lower, @"\bput\s+(?:them\s+)?in\s+folders?\b");
    }

    private static bool TryMatchCreateFolderAndMoveFilesCommand(string lower, out string folderName, out string fileType, out string? location)
    {
        folderName = string.Empty;
        fileType = string.Empty;
        location = null;

        var patterns = new[]
        {
            @"\b(?:create|make)\s+(?:a\s+)?folder\s+(?:called|named)?\s*(?<name>.+?)\b(?:\s+and\b|\s+to\b|\s+then\b|\s+which\b|\s+where\b|\s+with\b).*?\b(?:put|move)\b.*?\b(?<type>txt|text files|text file|pdf|pdfs|pdf files|word documents|word docs|docx|docs|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|presentations|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|files?)\b",
            @"\b(?:create|make)\s+(?:a\s+)?folder\s+(?:called|named)?\s*(?<name>.+?)\b.*?\b(?:put|move)\b.*?\b(?<type>txt|text files|text file|pdf|pdfs|pdf files|word documents|word docs|docx|docs|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|presentations|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|files?)\b"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(lower, pattern);
            if (!match.Success || !match.Groups["name"].Success)
                continue;

            folderName = match.Groups["name"].Value.Trim().TrimEnd('.', '?', '!', '"', '\'');
            if (match.Groups["type"].Success)
                fileType = match.Groups["type"].Value.Trim();

            var locMatch = Regex.Match(lower, @"\b(?:in|inside|at|within|on)\s+(?<loc>here|this folder|this directory|current folder|current directory|this location|this path|desktop|my desktop|desktop folder|pc|my pc|computer|my computer|downloads|downloads folder|documents|documents folder|pictures|pictures folder|videos|videos folder|music|music folder|[a-z]:\\[^?!]+|\\\\[^?!]+)\b");
            if (locMatch.Success && locMatch.Groups["loc"].Success)
                location = locMatch.Groups["loc"].Value.Trim();

            return true;
        }

        return false;
    }

    private static bool TryMatchCreateFolderCommand(string lower, out string folderName, out string? location)
    {
        folderName = string.Empty;
        location = null;

        if (string.IsNullOrWhiteSpace(lower)) return false;
        lower = lower.Replace("\r", " ").Replace("\n", " ").Trim();

        // Must start with a create/make/mkdir intent immediately followed by folder/dir.
        // This is anchored at the start of the message so that long AI responses that
        // happen to contain words like 'create' and 'folder' are NOT matched.
        if (!Regex.IsMatch(lower, @"^\s*(?:please\s+|can\s+you\s+|i\s+need\s+you\s+to\s+)?(?:create|make|build|add|mkdir)\s+(?:a\s+)?(?:new\s+)?(?:folder|directory|dir)\b|^\s*mkdir\b"))
        {
            return false;
        }

        // 1. Detect location
        if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?desktop\b"))
            location = "desktop";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?(?:pc|computer|machine)\b"))
            location = "desktop";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?documents?\b"))
            location = "documents";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?downloads?\b"))
            location = "downloads";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?pictures?\b"))
            location = "pictures";

        // 2. Extract raw folder name by stripping action, location, and name-marker phrases
        var cleaned = lower;

        cleaned = Regex.Replace(cleaned, @"^\b(?:i\s+need\s+you\s+to\s+|please\s+|can\s+you\s+|would\s+you\s+)?(?:create|make|build|add|mkdir)\s+(?:a\s+)?(?:new\s+)?(?:folder|directory|dir)\b", string.Empty, RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\b(?:on|in|inside|at)\s+(?:my\s+)?(?:desktop|pc|computer|machine|documents?|downloads?|pictures?|videos?|music)\b", string.Empty, RegexOptions.IgnoreCase);

        cleaned = Regex.Replace(cleaned, @"\b(?:and\s+)?(?:name\s+it|named\s+it|call\s+it|called\s+it|named\s+as|called\s+as|with\s+the\s+name|with\s+name|named|namd|nmd|naemd|called|caled|titled|title|name)\b", string.Empty, RegexOptions.IgnoreCase);

        cleaned = cleaned.Trim('.', '?', '!', '"', '\'', ' ', ':');

        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            folderName = cleaned;
            return true;
        }

        folderName = "New Folder";
        return true;
    }

    private static bool TryMatchCreateFileCommand(string lower, out string fileName, out string? location)
    {
        fileName = string.Empty;
        location = null;

        if (string.IsNullOrWhiteSpace(lower)) return false;
        lower = lower.Replace("\r", " ").Replace("\n", " ").Trim();

        if (!Regex.IsMatch(lower, @"^\s*(?:please\s+|can\s+you\s+|i\s+need\s+you\s+to\s+)?(?:create|make|add|write|touch)\s+(?:a\s+)?(?:new\s+)?(?:file|txt|text\s+file|doc|document)\b"))
        {
            return false;
        }

        if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?desktop\b"))
            location = "desktop";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?(?:pc|computer|machine)\b"))
            location = "desktop";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?documents?\b"))
            location = "documents";
        else if (Regex.IsMatch(lower, @"\b(?:on|in|inside|at)\s+(?:my\s+)?downloads?\b"))
            location = "downloads";

        var cleaned = lower;
        cleaned = Regex.Replace(cleaned, @"^\b(?:i\s+need\s+you\s+to\s+|please\s+|can\s+you\s+|would\s+you\s+)?(?:create|make|build|add|write|touch)\s+(?:a\s+)?(?:new\s+)?(?:text\s+)?(?:file|doc|document)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(?:on|in|inside|at)\s+(?:my\s+)?(?:desktop|pc|computer|machine|documents?|downloads?|pictures?|videos?|music)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(?:and\s+)?(?:name\s+it|named\s+it|call\s+it|called\s+it|named\s+as|called\s+as|with\s+the\s+name|with\s+name|named|namd|nmd|naemd|called|caled|titled|title|name)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = cleaned.Trim('.', '?', '!', '"', '\'', ' ', ':');

        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            fileName = cleaned;
            return true;
        }

        fileName = "New File.txt";
        return true;
    }

    private async Task<string> CreateFolderOnlyAsync(string basePath, string folderName)
    {
        if (!Directory.Exists(basePath))
            return "I couldn't find the target location to create the new folder.";

        folderName = Regex.Replace(folderName, @"[\\/:*?""<>|]+", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folderName))
            return "I couldn't determine the folder name. Please try again with a valid folder name.";

        var targetFolder = Path.Combine(basePath, folderName);
        if (Directory.Exists(targetFolder))
            return $"📁 A folder named '{folderName}' already exists in {Path.GetFileName(basePath)} ({targetFolder}).";

        Directory.CreateDirectory(targetFolder);
        return $"✅ Created folder '{folderName}' in {Path.GetFileName(basePath)} ({targetFolder}).";
    }

    private async Task<string> CreateFileOnlyAsync(string basePath, string fileName)
    {
        if (!Directory.Exists(basePath))
            return "I couldn't find the target location to create the new file.";

        fileName = Regex.Replace(fileName, @"[\\/:*?""<>|]+", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            return "I couldn't determine the file name. Please try again with a valid file name.";

        if (!Path.HasExtension(fileName))
            fileName += ".txt";

        var targetFile = Path.Combine(basePath, fileName);
        if (File.Exists(targetFile))
            return $"📄 A file named '{fileName}' already exists in {Path.GetFileName(basePath)} ({targetFile}).";

        await File.WriteAllTextAsync(targetFile, string.Empty);
        return $"✅ Created file '{fileName}' in {Path.GetFileName(basePath)} ({targetFile}).";
    }

    private string ResolveCreateFolderLocation(string? location)
    {
        if (!string.IsNullOrWhiteSpace(location))
        {
            if (IsLocalContextIndicator(location))
                return !string.IsNullOrEmpty(_lastListedFolderPath) ? _lastListedFolderPath : GetDefaultDesktopFolder();

            var resolved = ResolveKnownFolder(location);
            if (!string.IsNullOrEmpty(resolved)) return resolved;
        }

        return !string.IsNullOrEmpty(_lastListedFolderPath) ? _lastListedFolderPath : GetDefaultDesktopFolder();
    }

    private async Task<string> CreateFolderAndMoveFilesAsync(string basePath, string folderName, string fileType)
    {
        if (!Directory.Exists(basePath))
            return "I couldn't find the target location to create the new folder.";

        folderName = Regex.Replace(folderName, @"[\\/:*?""<>|]+", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folderName))
            return "I couldn't determine the folder name. Please try again with a valid folder name.";

        var targetFolder = Path.Combine(basePath, folderName);
        Directory.CreateDirectory(targetFolder);

        var extensions = GetExtensionsForType(fileType);
        if (extensions.Length == 0 && !string.IsNullOrWhiteSpace(fileType))
        {
            extensions = new[] { $"*{fileType.TrimStart('.')}", "*" };
        }

        var filesToMove = Directory.EnumerateFiles(basePath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !IsHiddenFile(new FileInfo(f)))
            .Where(f => extensions.Length == 1 && extensions[0] == "*" ? true : extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (filesToMove.Count == 0)
            return $"I couldn't find any {fileType} files in {Path.GetFileName(basePath)} to move into '{folderName}'.";

        var movedCount = 0;
        foreach (var file in filesToMove)
        {
            var destinationPath = Path.Combine(targetFolder, Path.GetFileName(file));
            destinationPath = GetUniqueDestinationPath(destinationPath);
            try
            {
                File.Move(file, destinationPath);
                movedCount++;
            }
            catch
            {
                // Skip files we can't move.
            }
        }

        if (movedCount == 0)
            return $"Created folder '{folderName}', but could not move any {fileType} files into it.";

        return $"✅ Created folder '{folderName}' and moved {movedCount} file{(movedCount == 1 ? string.Empty : "s")} into it.";
    }

    private static bool IsCountWordDocumentsCommand(string lower)
    {
        return Regex.IsMatch(lower, @"\bhow many\b.*\b(word )?(documents|docs|docx)\b(?!\s*folder)") ||
               Regex.IsMatch(lower, @"\b(word )?(documents|docs|docx)\b.*\bare there\b");
    }

    private static bool TryMatchFindFileCommand(string lower, out string searchTerm)
    {
        searchTerm = string.Empty;

        var patterns = new[]
        {
            @"\bfind\s+(?:me\s+)?(?<name>.+)$",
            @"\bfind\s+(?:the\s+)?(?:folder|directory)\s+(?<name>.+)$",
            @"\blocate\s+(?:the\s+)?(?<name>.+)$",
            @"\blocate\s+(?:the\s+)?(?:folder|directory)\s+(?<name>.+)$",
            @"\bsearch\s+for\s+(?<name>.+)$",
            @"\bsearch\s+for\s+(?:the\s+)?(?:folder|directory)\s+(?<name>.+)$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(lower, pattern);
            if (match.Success && match.Groups["name"].Success)
            {
                searchTerm = NormalizeFindFileSearchTerm(match.Groups["name"].Value.Trim().TrimEnd('.', '?', '!'));
                if (!string.IsNullOrWhiteSpace(searchTerm))
                    return true;
            }
        }

        return false;
    }

    private static string NormalizeFindFileSearchTerm(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return term;

        term = term.Trim();
        term = Regex.Replace(term, @"^\b(?:this|that|the|a|an)\s+file\s+", string.Empty, RegexOptions.IgnoreCase);
        term = Regex.Replace(term, @"^\b(?:this|that|the|a|an)\s+folder\s+", string.Empty, RegexOptions.IgnoreCase);
        term = Regex.Replace(term, @"^\b(?:this|that|the|a|an)\s+directory\s+", string.Empty, RegexOptions.IgnoreCase);
        term = Regex.Replace(term, @"^\b(?:this|that|the|a|an)\s+", string.Empty, RegexOptions.IgnoreCase);
        term = Regex.Replace(term, @"\bfiile\b", "file", RegexOptions.IgnoreCase);
        term = Regex.Replace(term, @"\bflie\b", "file", RegexOptions.IgnoreCase);
        return term.Trim();
    }

    private static bool IsListDownloadsCommand(string lower)
    {
        return Regex.IsMatch(lower, @"\b(?:what(?:'s| is| do i have| do we have)?|list|show)\b.*\bdownloads?\b");
    }

    private Task<string> ListDownloadsAsync()
    {
        if (!Directory.Exists(_downloadsFolder))
            return Task.FromResult("I couldn't find your Downloads folder to list its contents.");

        try
        {
            var entries = Directory.GetFileSystemEntries(_downloadsFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(p => !IsHiddenFile(new FileInfo(p)))
                .OrderBy(p => p)
                .ToList();

            if (entries.Count == 0)
                return Task.FromResult("Your Downloads folder is empty.");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📂 Downloads ({entries.Count} items)");
            sb.AppendLine();

            var take = Math.Min(entries.Count, 10);
            for (int i = 0; i < take; i++)
            {
                var name = Path.GetFileName(entries[i]);
                sb.AppendLine($"  • {FormatClickableFileLink(entries[i], name)}");
            }

            if (entries.Count > take)
                sb.AppendLine($"\n... and {entries.Count - take} more items.");

            sb.AppendLine("\n💡 Tip: Say \"open [filename]\" to open any file.");
            return Task.FromResult(sb.ToString());
        }
        catch
        {
            return Task.FromResult("I couldn't read your Downloads folder. There may be a permissions issue.");
        }
    }

    private static string FormatClickableFileLink(string path, string label)
    {
        return $"[CLICK:{Uri.EscapeDataString(path)}|{label}]";
    }

    private Task<string> OrganizeAsync(string path)
    {
        if (!Directory.Exists(path))
            return Task.FromResult("I couldn't find that folder, so I can't organize it.");

        var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly)
            .Where(f => !IsHiddenFile(new FileInfo(f)))
            .ToList();

        if (files.Count == 0)
            return Task.FromResult("That folder is already empty or contains no files to organize.");

        var movedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var destinationDirectory = GetDestinationDirectoryForFile(file);
            if (string.IsNullOrEmpty(destinationDirectory))
                destinationDirectory = Path.Combine(path, "Others");

            Directory.CreateDirectory(destinationDirectory);
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(file));
            destinationPath = GetUniqueDestinationPath(destinationPath);

            try
            {
                File.Move(file, destinationPath);
                movedCounts.TryGetValue(Path.GetFileName(destinationDirectory), out var count);
                movedCounts[Path.GetFileName(destinationDirectory)] = count + 1;
            }
            catch
            {
                // Skip files we can't move.
            }
        }

        if (movedCounts.Count == 0)
            return Task.FromResult("I could not move any files in that folder. Check if the files are in use or if permissions are restricted.");

        var summary = string.Join(", ", movedCounts.OrderBy(kvp => kvp.Key)
            .Select(kvp => $"{kvp.Value} file{(kvp.Value == 1 ? string.Empty : "s")} into {kvp.Key}"));

        return Task.FromResult($"✅ Organized successfully! {summary}.");
    }

    private Task<string> CountWordDocumentsAsync()
    {
        if (!Directory.Exists(_downloadsFolder))
            return Task.FromResult("I couldn't find your Downloads folder to count Word documents.");

        var count = Directory.EnumerateFiles(_downloadsFolder, "*.*", SearchOption.AllDirectories)
            .Count(f => string.Equals(Path.GetExtension(f), ".doc", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(Path.GetExtension(f), ".docx", StringComparison.OrdinalIgnoreCase));

        var result = count == 0
            ? "I couldn't find any Word documents in your Downloads folder."
            : $"There {(count == 1 ? "is" : "are")} {count} Word document{(count == 1 ? string.Empty : "s")} in your Downloads folder.";

        return Task.FromResult(result);
    }

    private Task<string> FindFileAsync(string searchTerm)
    {
        searchTerm = NormalizeFindFileSearchTerm(searchTerm);
        if (string.IsNullOrWhiteSpace(searchTerm))
            return Task.FromResult("Please tell me the file name you want to find.");

        var searchRoots = new[]
        {
            GetDefaultDesktopFolder(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            _downloadsFolder,
            Environment.CurrentDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        }
        .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var matches = new List<string>();
        foreach (var root in searchRoots)
        {
            try
            {
                matches.AddRange(EnumerateFilesRecursive(root, searchTerm, 5));
            }
            catch
            {
                // Ignore folders we can't access.
            }
            if (matches.Count > 0)
                break;
        }

        if (matches.Count == 0 && searchTerm.Contains(' '))
        {
            var fallbackTerm = Path.GetFileName(searchTerm);
            if (!string.IsNullOrWhiteSpace(fallbackTerm) && !fallbackTerm.Equals(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var root in searchRoots)
                {
                    try
                    {
                        matches.AddRange(EnumerateFilesRecursive(root, fallbackTerm, 5));
                    }
                    catch
                    {
                        // Ignore folders we can't access.
                    }
                    if (matches.Count > 0)
                        break;
                }
            }
        }

        var uniqueMatches = matches.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path)
            .ToList();

        string result;
        if (uniqueMatches.Count == 0)
            result = $"❌ Couldn't find '{searchTerm}' in your searchable folders.";
        else if (uniqueMatches.Count == 1)
            result = $"✅ Found: {FormatClickableFileLink(uniqueMatches[0], Path.GetFileName(uniqueMatches[0]))}\n💡 Say \"open {Path.GetFileNameWithoutExtension(uniqueMatches[0])}\" to open it.";
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"🔍 Found {uniqueMatches.Count} matching files:");
            var take = Math.Min(uniqueMatches.Count, 10);
            for (int i = 0; i < take; i++)
                sb.AppendLine($"  • {FormatClickableFileLink(uniqueMatches[i], Path.GetFileName(uniqueMatches[i]))}");
            if (uniqueMatches.Count > take) sb.AppendLine($"  ... and {uniqueMatches.Count - take} more");
            sb.AppendLine($"\n💡 Say \"open [filename]\" to open any of these.");
            result = sb.ToString();
        }

        return Task.FromResult(result);
    }

    private IEnumerable<string> EnumerateFilesRecursive(string root, string searchTerm, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(root) || maxDepth < 0) return Array.Empty<string>();

        var found = new List<string>();
        var queue = new Queue<(string path, int depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (path, depth) = queue.Dequeue();
            if (depth > maxDepth) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*"))
                {
                    if (Path.GetFileName(file).Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileNameWithoutExtension(file).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        found.Add(file);
                }
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    if (Path.GetFileName(dir).Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        found.Add(dir);
                }
            }
            catch
            {
                // Skip inaccessible directories.
            }

            if (depth == maxDepth) continue;

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    if (!IsHiddenFile(new FileInfo(dir)))
                        queue.Enqueue((dir, depth + 1));
                }
            }
            catch
            {
                // Skip inaccessible directories.
            }
        }

        return found;
    }

    private static bool TryMatchListLocationCommand(string lower, out string location)
    {
        location = string.Empty;
        var m = Regex.Match(lower, @"\b(?:what do i have|what do i have on|what is on|what's on|whats on|what is in|whats in|list|show|what(?:'s| is)|display|give me|give me all|grab|find all|show me all)\b.*\b(?<loc>(?:[a-z]:\\[^?!]+|\\\\[^?!]+|downloads|downloads folder|desktop|desktop folder|documents?|document(?: folder)?|pictures|pictures folder|videos|videos folder|music|music folder|appdata|application data|program files|program files x86|windows|system32|user profile|home|my downloads|my desktop|my documents|my pictures|my videos|my music|this pc|my computer|computer|pc|everything|everywhere|all files|all folders|whole pc|whole computer|whole machine))\b");
        if (m.Success && m.Groups["loc"].Success)
        {
            location = m.Groups["loc"].Value;
            return true;
        }
        return false;
    }

    private static bool TryMatchCountCommand(string lower, out string? countType, out string? location)
    {
        countType = null; location = null;
        var patterns = new[]
        {
            @"\b(?:how many|count|what(?:'s| is) the number of|number of)\b.*?\b(?<type>word documents|word docs|word files|doc files|documents|docs|docx|pdf|pdfs|pdf files|txt|txt files|text files|text file|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|folders|subfolders|directories|directory|files|items)\b.*?\b(?:in|on|from|at|inside|within)\b\s*(?<loc>(?:[a-z]:\\[^?!]+|\\\\[^?!]+|downloads|downloads folder|desktop|desktop folder|documents?|document(?: folder)?|pictures|pictures folder|videos|videos folder|music|music folder|appdata|application data|program files|program files x86|windows|system32|user profile|home|my downloads|my desktop|my documents|my pictures|my videos|my music))\b",
            @"\b(?:how many|count|what(?:'s| is) the number of|number of)\b.*?\b(?<type>word documents|word docs|word files|doc files|documents|docs|docx|pdf|pdfs|pdf files|txt|txt files|text files|text file|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|folders|subfolders|directories|directory|files|items)\b.*?\b(?<loc>(?:[a-z]:\\[^?!]+|\\\\[^?!]+|downloads|downloads folder|desktop|desktop folder|documents?|document(?: folder)?|pictures|pictures folder|videos|videos folder|music|music folder|appdata|application data|program files|program files x86|windows|system32|user profile|home|my downloads|my desktop|my documents|my pictures|my videos|my music))\b"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(lower, pattern);
            if (!m.Success) continue;

            countType = m.Groups["type"].Value;
            if (m.Groups["loc"].Success)
                location = m.Groups["loc"].Value;
            return true;
        }

        return false;
    }

    private static bool TryMatchListFilesByTypeAndLocation(string lower, out string fileType, out string location)
    {
        fileType = string.Empty;
        location = string.Empty;

        var patterns = new[]
        {
            @"\b(?:list|show|find|search for|search|what(?:'s| is)|display|give me|give me all|grab|find all|show me all)\b.*\b(?<type>everything|every file|all files|all of it|all of them|every crevice|word documents|word docs|word files|doc files|documents|docs|docx|docx files|pdfs|pdf files|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|presentations|text files|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|executables|installers|apps|config files)\b.*\b(?:on|in|from|at|inside)\b.*\b(?<loc>downloads|desktop|documents|pictures|videos|music|appdata|application data|program files|program files x86|windows|system32|user profile|home|my downloads|my desktop|my documents|my pictures|my videos|my music|this pc|my computer|computer|pc|everything|everywhere|all files|all folders|whole pc|whole computer|whole machine)\b",
            @"\b(?<type>everything|every file|all files|all of it|all of them|every crevice|word documents|word docs|word files|doc files|documents|docs|docx|docx files|pdfs|pdf files|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|presentations|text files|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|executables|installers|apps|config files)\b.*\b(?:on|in|from|at|inside)\b.*\b(?<loc>downloads|desktop|documents|pictures|videos|music|appdata|application data|program files|program files x86|windows|system32|user profile|home|my downloads|my desktop|my documents|my pictures|my videos|my music|this pc|my computer|computer|pc|everything|everywhere|all files|all folders|whole pc|whole computer|whole machine)\b",
            @"\b(?:list|show|find|search for|what(?:'s| is)|display|give me|give me all|grab|find all|show me all)\b.*\b(?<loc>downloads|desktop|documents|pictures|videos|music|appdata|application data|program files|program files x86|windows|system32|user profile|home|my downloads|my desktop|my documents|my pictures|my videos|my music|this pc|my computer|computer|pc|everything|everywhere|all files|all folders|whole pc|whole computer|whole machine)\b.*\b(?<type>everything|every file|all files|all of it|all of them|every crevice|word documents|word docs|word files|doc files|documents|docs|docx|docx files|pdfs|pdf files|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|presentations|text files|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts|executables|installers|apps|config files)\b"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(lower, pattern);
            if (m.Success && m.Groups["type"].Success)
            {
                fileType = m.Groups["type"].Value;
                if (m.Groups["loc"].Success)
                    location = m.Groups["loc"].Value;

                return true;
            }
        }

        return false;
    }

    private static string[] GetExtensionsForType(string fileType)
    {
        fileType = fileType.ToLowerInvariant();
        if (fileType.Contains("everything") || fileType.Contains("every file") || fileType.Contains("all files") || fileType.Contains("all of it") || fileType.Contains("all of them") || fileType.Contains("every crevice"))
            return new[] { "*" };
        if (fileType.Contains("word") || fileType.Contains("docx") || fileType.Contains("doc"))
            return new[] { ".doc", ".docx" };
        if (fileType.Contains("pdf"))
            return new[] { ".pdf" };
        if (fileType.Contains("image") || fileType.Contains("photo") || fileType.Contains("picture") || fileType.Contains("screenshot") || fileType.Contains("jpg") || fileType.Contains("png"))
            return new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff" };
        if (fileType.Contains("excel") || fileType.Contains("spreadsheet") || fileType.Contains("xls") || fileType.Contains("csv"))
            return new[] { ".xls", ".xlsx", ".csv" };
        if (fileType.Contains("ppt") || fileType.Contains("powerpoint") || fileType.Contains("presentation"))
            return new[] { ".ppt", ".pptx" };
        if (fileType.Contains("text") || fileType.Contains("txt") || fileType.Contains("note") || fileType.Contains("markdown") || fileType.Contains("md") || fileType.Contains("log") || fileType.Contains("rtf"))
            return new[] { ".txt", ".md", ".csv", ".rtf", ".log" };
        if (fileType.Contains("archive") || fileType.Contains("zip") || fileType.Contains("rar") || fileType.Contains("7z") || fileType.Contains("tar") || fileType.Contains("gz") || fileType.Contains("bz2"))
            return new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".iso" };
        if (fileType.Contains("video") || fileType.Contains("movie") || fileType.Contains("movies") || fileType.Contains("mp4") || fileType.Contains("mkv") || fileType.Contains("mov") || fileType.Contains("avi"))
            return new[] { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".webm" };
        if (fileType.Contains("audio") || fileType.Contains("music") || fileType.Contains("mp3") || fileType.Contains("wav") || fileType.Contains("flac") || fileType.Contains("m4a") || fileType.Contains("aac"))
            return new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg" };
        if (fileType.Contains("code") || fileType.Contains("source") || fileType.Contains("script") || fileType.Contains("cs") || fileType.Contains("js") || fileType.Contains("ts") || fileType.Contains("py") || fileType.Contains("java") || fileType.Contains("cpp") || fileType.Contains("h") || fileType.Contains("go") || fileType.Contains("rb") || fileType.Contains("php") || fileType.Contains("html") || fileType.Contains("css") || fileType.Contains("json") || fileType.Contains("xml") || fileType.Contains("yaml") || fileType.Contains("yml") || fileType.Contains("sql") || fileType.Contains("ps1") || fileType.Contains("bat") || fileType.Contains("sh"))
            return new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".h", ".hpp", ".go", ".rb", ".php", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".ps1", ".bat", ".sh" };
        if (fileType.Contains("exe") || fileType.Contains("installer") || fileType.Contains("installers") || fileType.Contains("app") || fileType.Contains("executable"))
            return new[] { ".exe", ".msi", ".bat", ".cmd", ".dll", ".lnk" };
        if (fileType.Contains("config") || fileType.Contains("settings") || fileType.Contains("ini") || fileType.Contains("cfg"))
            return new[] { ".json", ".xml", ".ini", ".cfg", ".yaml", ".yml" };
        if (fileType.Contains("database") || fileType.Contains("db") || fileType.Contains("sqlite") || fileType.Contains("sql"))
            return new[] { ".db", ".sqlite", ".sqlite3", ".sql" };
        if (fileType.Contains("font"))
            return new[] { ".ttf", ".otf" };
        return Array.Empty<string>();
    }

    private async Task<string> ListFilesByTypeInLocationAsync(string path, string fileType)
    {
        if (!Directory.Exists(path))
            return "That folder does not exist.";

        var extensions = GetExtensionsForType(fileType);
        if (extensions.Length == 0)
            return $"I don't recognize the file type '{fileType}', but I can still list files if you ask more specifically.";

        var listAll = extensions.Length == 1 && extensions[0] == "*";
        var files = Directory.EnumerateFiles(path, "*", listAll ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => listAll || extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0)
            return $"I couldn't find any {fileType} in {Path.GetFileName(path)}.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"📄 {fileType} on {Path.GetFileName(path)} ({files.Count} files)");
        sb.AppendLine();

        var take = Math.Min(files.Count, 20);
        for (int i = 0; i < take; i++)
            sb.AppendLine($"  • {FormatClickableFileLink(files[i], Path.GetFileName(files[i]))}");

        if (files.Count > take)
            sb.AppendLine($"\n... and {files.Count - take} more files.");

        sb.AppendLine($"\n💡 Tip: Say \"open [filename]\" to open any file.");
        return await Task.FromResult(sb.ToString());
    }

    private static bool TryMatchExistenceCommand(string lower, out string? fileType, out string? location)
    {
        fileType = null;
        location = null;

        var patterns = new[]
        {
            @"\b(?:any|is there|are there|do i have|have i got|have i any|does .* have|find|search for|look for|see if there are)\b.*\b(?<type>word documents|word docs|word files|docs|doc files|documents|docx|docx files|pdfs|pdf files|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|text files|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts)\b(?:.*\b(?:on|in|from|at|inside)\b.*\b(?<loc>downloads|desktop|documents|pictures|videos|music|downloads folder|desktop folder|documents folder|pictures folder|videos folder|music folder|my downloads|my desktop|my documents|my pictures|my videos|my music)\b)?",
            @"\b(?<type>word documents|word docs|word files|docs|doc files|documents|docx|docx files|pdfs|pdf files|images|image files|photos|pictures|screenshots|spreadsheets|excel files|xls|xlsx|powerpoint|ppt|pptx|text files|notes|markdown|md|csv|rtf|archives|zip|rar|7z|videos|movies|audio|music|code|source files|scripts)\b.*\b(?:on|in|from|at|inside)\b.*\b(?<loc>downloads|desktop|documents|pictures|videos|music|downloads folder|desktop folder|documents folder|pictures folder|videos folder|music folder|my downloads|my desktop|my documents|my pictures|my videos|my music)\b"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(lower, pattern);
            if (m.Success && m.Groups["type"].Success)
            {
                fileType = m.Groups["type"].Value;
                if (m.Groups["loc"].Success)
                    location = m.Groups["loc"].Value;
                return true;
            }
        }

        return false;
    }

    private async Task<string> ExistsInLocationAsync(string path, string? fileType)
    {
        if (!Directory.Exists(path))
            return "That folder does not exist.";

        if (string.IsNullOrWhiteSpace(fileType))
            return "Please specify what you're looking for.";

        fileType = fileType.ToLowerInvariant();
        IEnumerable<string> matches = Array.Empty<string>();

        if (fileType.Contains("word") || fileType.Contains("docx"))
        {
            matches = SafeEnumerateFiles(path).Where(f => f.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)
                                                    || f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase));
        }
        else if (fileType.Contains("pdf"))
        {
            matches = SafeEnumerateFiles(path).Where(f => f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        }
        else if (fileType.Contains("image") || fileType.Contains("jpg") || fileType.Contains("png"))
        {
            var exts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
            matches = SafeEnumerateFiles(path)
                .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            matches = SafeEnumerateFiles(path)
                .Where(p => Path.GetFileName(p).Contains(fileType, StringComparison.OrdinalIgnoreCase));
        }

        var first = matches.FirstOrDefault();
        if (first == null)
            return $"No {fileType} found in {Path.GetFileName(path)}.";

        return $"Yes — found: {GetRelativePath(first)}";
    }

    private static string ResolveKnownFolder(string? loc)
    {
        if (string.IsNullOrWhiteSpace(loc)) return GetDefaultDesktopFolder();
        loc = loc.ToLowerInvariant().Trim();
        if (Directory.Exists(loc))
            return loc;

        if (Regex.IsMatch(loc, @"^[a-z]:(\\|/)|^\\\\", RegexOptions.IgnoreCase))
            return loc;

        if (loc.Contains("download")) return GetDefaultDownloadsFolder();
        if (loc.Contains("desktop") || loc.Contains("pc") || loc.Contains("computer") || loc.Contains("machine")) return GetDefaultDesktopFolder();
        if (loc.Contains("document")) return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (loc.Contains("picture") || loc.Contains("photo") || loc.Contains("image")) return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (loc.Contains("video") || loc.Contains("movie")) return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (loc.Contains("music") || loc.Contains("audio")) return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        return GetDefaultDesktopFolder();
    }

    private static string GetDefaultDesktopFolder()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private static bool IsLocalContextIndicator(string loc)
    {
        if (string.IsNullOrWhiteSpace(loc))
            return false;

        loc = loc.ToLowerInvariant().Trim();
        return loc == "here" || loc == "this folder" || loc == "this directory" || loc == "current folder" || loc == "current directory" || loc == "this location" || loc == "this path";
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            yield break;

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                var isHidden = false;
                try
                {
                    isHidden = IsHiddenFile(new FileInfo(file));
                }
                catch
                {
                    continue;
                }

                if (!isHidden)
                    yield return file;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var subdir in subdirs)
            {
                var isHidden = false;
                try
                {
                    isHidden = IsHiddenFile(new FileInfo(subdir));
                }
                catch
                {
                    continue;
                }

                if (!isHidden)
                    stack.Push(subdir);
            }
        }
    }

    private string? ResolveCountLocation(string loc)
    {
        if (string.IsNullOrWhiteSpace(loc))
            return null;

        if (IsLocalContextIndicator(loc))
            return !string.IsNullOrEmpty(_lastListedFolderPath) ? _lastListedFolderPath : Environment.CurrentDirectory;

        return ResolveKnownFolder(loc);
    }

    private async Task<string> ListLocationAsync(string path)
    {
        if (!Directory.Exists(path)) return "That folder does not exist.";
        try
        {
            var entries = Directory.GetFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
                .Where(p => !IsHiddenFile(new FileInfo(p)))
                .OrderBy(p => p)
                .ToList();

            if (entries.Count == 0) return "That folder is empty.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📂 {Path.GetFileName(path) ?? path} ({entries.Count} items)");
            sb.AppendLine();
            var take = Math.Min(entries.Count, 50);
            for (int i = 0; i < take; i++)
            {
                var name = Path.GetFileName(entries[i]);
                var isDir = Directory.Exists(entries[i]);
                var typeLabel = isDir ? "📁" : "📄";
                sb.AppendLine($"{typeLabel} {FormatClickableFileLink(entries[i], name)}");
            }
            if (entries.Count > take) sb.AppendLine($"\n... and {entries.Count - take} more items.");
            sb.AppendLine($"\n💡 Tip: Say \"open [filename]\" to open any file.");
            return await Task.FromResult(sb.ToString());
        }
        catch
        {
            return "I couldn't read that folder (permissions or IO error).";
        }
    }

    private async Task<string> ListDirectoriesInLocationAsync(string path)
    {
        if (!Directory.Exists(path)) return "That folder does not exist.";

        try
        {
            var directories = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !IsHiddenFile(new FileInfo(d)))
                .OrderBy(d => d)
                .ToList();

            if (directories.Count == 0)
                return $"There are no folders in {Path.GetFileName(path)}.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📁 Folders in {Path.GetFileName(path) ?? path} ({directories.Count})");
            sb.AppendLine();

            var take = Math.Min(directories.Count, 50);
            for (int i = 0; i < take; i++)
            {
                var name = Path.GetFileName(directories[i]);
                sb.AppendLine($"  • {FormatClickableFileLink(directories[i], name)}");
            }
            if (directories.Count > take)
                sb.AppendLine($"\n... and {directories.Count - take} more folders.");

            sb.AppendLine($"\n💡 Tip: Say \"show [folder name]\" to list a specific folder.");
            return await Task.FromResult(sb.ToString());
        }
        catch
        {
            return "I couldn't read that folder (permissions or IO error).";
        }
    }

    private async Task<string> CountFilesInLocationAsync(string path, string? countType)
    {
        if (!Directory.Exists(path)) return "That folder does not exist.";
        try
        {
            var files = SafeEnumerateFiles(path).ToList();
            int count = 0;
            if (string.IsNullOrEmpty(countType))
            {
                count = files.Count;
                return await Task.FromResult($"There are {count} files in {Path.GetFileName(path)}.");
            }

            countType = countType.ToLowerInvariant();
            if (countType.Contains("word"))
            {
                count = files.Count(f => string.Equals(Path.GetExtension(f), ".doc", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(Path.GetExtension(f), ".docx", StringComparison.OrdinalIgnoreCase));
                return await Task.FromResult($"There {(count==1?"is":"are")} {count} Word document{(count==1?"":"s")} in {Path.GetFileName(path)}.");
            }
            if (countType.Contains("pdf"))
            {
                count = files.Count(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase));
                return await Task.FromResult($"There {(count==1?"is":"are")} {count} PDF file{(count==1?"":"s")} in {Path.GetFileName(path)}.");
            }
            if (countType.Contains("txt") || countType.Contains("text"))
            {
                var exts = new[] { ".txt", ".md", ".csv", ".rtf", ".log" };
                count = files.Count(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                return await Task.FromResult($"There {(count==1?"is":"are")} {count} text file{(count==1?"":"s")} in {Path.GetFileName(path)}.");
            }
            if (countType.Contains("image") || countType.Contains("jpg") || countType.Contains("png") || countType.Contains("images"))
            {
                var exts = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                count = files.Count(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                return await Task.FromResult($"There {(count==1?"is":"are")} {count} image{(count==1?"":"s")} in {Path.GetFileName(path)}.");
            }

            if (countType.Contains("folder") || countType.Contains("folders") || countType.Contains("directory") || countType.Contains("directories") || countType.Contains("subfolder") || countType.Contains("subfolders"))
            {
                var dirs = Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                    .Where(d => !IsHiddenFile(new FileInfo(d)))
                    .Count();
                return await Task.FromResult($"There {(dirs==1?"is":"are")} {dirs} folder{(dirs==1?"":"s")} on {Path.GetFileName(path)}.");
            }

            count = files.Count;
            return await Task.FromResult($"There are {count} files in {Path.GetFileName(path)}.");
        }
        catch
        {
            return "I couldn't count files in that folder (permissions or IO error).";
        }
    }

    private string GetDestinationDirectoryForFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        foreach (var pair in DownloadCategories)
        {
            if (pair.Value.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return Path.Combine(_downloadsFolder, pair.Key);
            }
        }

        return Path.Combine(_downloadsFolder, "Others");
    }

    private static bool IsHiddenFile(FileInfo fileInfo)
    {
        return fileInfo.Name.StartsWith('.') || (fileInfo.Attributes & FileAttributes.Hidden) != 0;
    }

    private static string GetUniqueDestinationPath(string destinationPath)
    {
        if (!File.Exists(destinationPath))
            return destinationPath;

        var directory = Path.GetDirectoryName(destinationPath)!;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var counter = 1;

        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{fileNameWithoutExtension} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(candidate));

        return candidate;
    }

    private string GetRelativePath(string path)
    {
        if (path.StartsWith(_downloadsFolder, StringComparison.OrdinalIgnoreCase))
            return path[_downloadsFolder.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return path;
    }

    private static string GetDefaultDownloadsFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var knownPath = GetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"));
                if (!string.IsNullOrWhiteSpace(knownPath))
                    return knownPath;
            }
            catch
            {
                // Fallback below
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static string GetKnownFolderPath(Guid knownFolderId)
    {
        var result = SHGetKnownFolderPath(knownFolderId, 0, IntPtr.Zero, out var pathPtr);
        if (result != 0)
            throw new InvalidOperationException($"SHGetKnownFolderPath failed: {result}");

        try
        {
            return Marshal.PtrToStringUni(pathPtr) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
        }
    }

    private static bool TryMatchListLastResultCommand(string lower)
    {
        return Regex.IsMatch(lower, @"\b(?:list|show|display|what(?:'s| is)|show me|list me)\b.*\b(?:them|those|the folders|the directories|the files|the items)\b");
    }

    private static bool TryMatchListSpecificFolderCommand(string lower, out string folderName)
    {
        folderName = string.Empty;
        
        if (Regex.IsMatch(lower, @"\b(?:list|show|what.?s in)\b.*\b(?:that|this)\s+folder\b"))
        {
            folderName = "that";
            return true;
        }

        var patterns = new[]
        {
            @"\b(?:list|show|what.?s in)\b\s+(?:that|the)?\s*(?<name>\w+(?:\s+\w+)?)\s*(?:folder)?",
            @"\b(?:list|show)\b\s+(?<name>[^?!.]+)$"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(lower, pattern);
            if (m.Success && m.Groups["name"].Success)
            {
                folderName = m.Groups["name"].Value.Trim().TrimEnd('?', '!', '.');
                if (!string.IsNullOrWhiteSpace(folderName) && folderName != "that" && folderName != "this")
                    return true;
            }
        }
        return false;
    }

    private string? FindSubfolderInDownloads(string folderNameHint)
    {
        if (!Directory.Exists(_downloadsFolder)) return null;
        try
        {
            var subfolders = Directory.GetDirectories(_downloadsFolder)
                .Where(p => !IsHiddenFile(new FileInfo(p)))
                .ToList();

            if (subfolders.Count == 0) return null;

            if (folderNameHint == "that" || folderNameHint == "this")
            {
                var sorted = subfolders.OrderByDescending(p => Directory.GetLastWriteTime(p)).ToList();
                return sorted.FirstOrDefault();
            }

            var exact = subfolders.FirstOrDefault(p => Path.GetFileName(p).Equals(folderNameHint, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(exact)) return exact;

            var partial = subfolders.FirstOrDefault(p => Path.GetFileName(p).Contains(folderNameHint, StringComparison.OrdinalIgnoreCase));
            return partial;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryMatchShellCommand(string lower, out string shellCommand)
    {
        shellCommand = string.Empty;
        var patterns = new[]
        {
            @"\brun\s+(?<cmd>.+)$",
            @"\bexecute\s+(?<cmd>.+)$",
            @"\b(?:run|execute)\s+(?:command|cmd|powershell|shell|terminal)\s+(?<cmd>.+)$",
            @"\b(?:shell|terminal)\s+(?:command\s+)?(?<cmd>.+)$",
            @"^\s*(?<cmd>(?:cmd|powershell)\s+.+)$",
            @"^\s*(?<cmd>(?:sfc|chkdsk|ipconfig|netsh|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|route|nslookup|shutdown|get-process|get-service|get-eventlog|get-childitem|dir|cd|mkdir|rmdir|del|copy|move)\b.+)$",
            @"^\s*(?<cmd>[^\r\n?!.]+[\\/][^\r\n]+)\s*$"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(lower, pattern, RegexOptions.IgnoreCase);
            if (!match.Success || !match.Groups["cmd"].Success)
                continue;

            shellCommand = match.Groups["cmd"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(shellCommand))
                return true;
        }

        return false;
    }

    private bool TryMatchPerformOptionCommand(string lower, out int optionIndex)
    {
        optionIndex = 0;
        var patterns = new[]
        {
            @"\b(?:perform|do|run|execute|try|pick|use)\s*(?:option\s*)?(?<num>\d+)\b",
            @"^\s*(?:okay|ok|yes|sure|right|fine|go ahead)\s+(?:option\s*)?(?<num>\d+)\b",
            @"\boption\s*(?:number\s*)?(?<num>\d+)\b",
            @"\b(?:okay|ok|yes|sure|right|fine|go ahead)\b.*\boption\s*(?:number\s*)?(?<num>\d+)\b"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(lower, pattern, RegexOptions.IgnoreCase);
            if (!match.Success || !match.Groups["num"].Success)
                continue;

            if (int.TryParse(match.Groups["num"].Value, out optionIndex))
                return true;
        }

        return false;
    }

    private bool TryMatchRunPreviousShellCommand(string lower, out string pendingShellCommand)
    {
        pendingShellCommand = string.Empty;
        if (_pendingShellCommand is null)
            return false;

        if (Regex.IsMatch(lower, @"\b(?:okay|ok|yes|yeah|sure|please|go ahead|just)\b.*\b(?:run|execute)\b|\b(?:run|execute)\s+(?:it|that|the command)\b|\b(?:execute|run)\s+the\s+previous\s+command\b", RegexOptions.IgnoreCase))
        {
            pendingShellCommand = _pendingShellCommand;
            _pendingShellCommand = null;
            return true;
        }

        return false;
    }

    private async Task<string> ExecuteShellCommandAsync(string shellCommand)
    {
        try
        {
            var escapedCommand = shellCommand.Trim();
            if (string.IsNullOrWhiteSpace(escapedCommand))
                return "No shell command was specified.";

            if (CommandRequiresAdmin(escapedCommand))
            {
                return await ExecuteElevatedShellCommandAsync(escapedCommand);
            }

            // Long-running commands that need progress tracking → open visible terminal
            if (CommandNeedsVisibleTerminal(escapedCommand))
            {
                return await ExecuteVisibleShellCommandAsync(escapedCommand);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C " + escapedCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = !string.IsNullOrWhiteSpace(WorkspaceDirectory) ? WorkspaceDirectory : GetDefaultDesktopFolder()
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return "Could not start the shell command.";

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());

            var output = outputTask.Result.Trim();
            var error = errorTask.Result.Trim();
            if (!string.IsNullOrEmpty(error))
                return $"Command finished with error:\n{error}";

            if (!string.IsNullOrEmpty(output))
                return output;

            return "Command executed successfully with no output.";
        }
        catch (Exception ex)
        {
            return $"Failed to execute shell command: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns true for commands that are long-running and need a visible terminal window
    /// so the user can track progress (e.g. sfc, dism, chkdsk, defrag).
    /// Quick commands like ipconfig, ping, systeminfo return inline in chat.
    /// </summary>
    private static bool CommandNeedsVisibleTerminal(string command)
    {
        return Regex.IsMatch(command,
            @"^\s*(?:sfc|dism|chkdsk|gpupdate|shutdown|robocopy|xcopy|format|diskpart|defrag|wmic|bcdedit|bootrec|schtasks|fsutil|secedit)\b",
            RegexOptions.IgnoreCase);
    }

    private static bool CommandRequiresAdmin(string command)
    {
        return Regex.IsMatch(command, @"^\s*(?:sfc|chkdsk|bcdedit|bootrec|netsh|reg|sc|schtasks|diskpart|wmic|gpupdate|shutdown|rundll32|secedit|mountvol|icacls|takeown|format|fsutil|gpresult|dism|defrag)\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(command, @"\b(?:/scannow|/f|/fix|/repair|/reset|/restore|/force|/all|/rebuildbcd|/fixmbr|/fixboot|/restorehealth|/cleanup-image)\b", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Opens a visible cmd.exe window so the user can track progress of long-running commands.
    /// Uses /K so the window stays open after the command finishes.
    /// </summary>
    private async Task<string> ExecuteVisibleShellCommandAsync(string shellCommand)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/K " + shellCommand,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = !string.IsNullOrWhiteSpace(WorkspaceDirectory) ? WorkspaceDirectory : GetDefaultDesktopFolder()
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return "Could not open the terminal window.";

            return $"🖥️ Opened a terminal window running `{shellCommand}`.\n\nYou can track its progress in the terminal. The window will stay open when it finishes.";
        }
        catch (Exception ex)
        {
            return $"Failed to open terminal: {ex.Message}";
        }
    }

    /// <summary>
    /// Runs a command elevated (as admin) in a visible terminal window.
    /// Uses /K so the window stays open after the command finishes for review.
    /// </summary>
    private async Task<string> ExecuteElevatedShellCommandAsync(string shellCommand)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/K " + shellCommand,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = !string.IsNullOrWhiteSpace(WorkspaceDirectory) ? WorkspaceDirectory : GetDefaultDesktopFolder()
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return "Could not start the elevated shell command.";

            return $"🖥️ Launched `{shellCommand}` with **administrator privileges** in a terminal window.\n\nWindows may ask you to approve the action. The terminal will stay open when the command finishes.";
        }
        catch (Exception ex)
        {
            return $"Unable to elevate the command: {ex.Message}. You may need to run the app as an administrator.";
        }
    }

    private static bool TryMatchOpenFileCommand(string lower, out string fileName)
    {
        fileName = string.Empty;
        var patterns = new[]
        {
            @"\bopen\s+(?<name>.+)$",
            @"\blaunch\s+(?<name>.+)$",
            @"\bstart\s+(?:the\s+)?(?:app(?:lication)?|program|file|folder|directory)\s+(?<name>.+)$"
        };

        foreach (var pattern in patterns)
        {
            var m = Regex.Match(lower, pattern);
            if (m.Success && m.Groups["name"].Success)
            {
                fileName = m.Groups["name"].Value.Trim().TrimEnd('?', '!', '.');
                if (!string.IsNullOrWhiteSpace(fileName))
                    return true;
            }
        }
        return false;
    }

    private static bool TryMatchDeleteCommand(string lower, out string targetName)
    {
        targetName = string.Empty;
        var m = Regex.Match(lower, @"\b(?:delete|remove|trash|erase)\b\s+(?:the\s+)?(?:file|folder|directory\s+)?(?:named|called\s+)?[""']?(?<name>[^""'!?.]+)[""']?", RegexOptions.IgnoreCase);
        if (m.Success && m.Groups["name"].Success)
        {
            targetName = m.Groups["name"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(targetName)) return true;
        }
        return false;
    }

    private static bool TryMatchRenameCommand(string lower, out string oldName, out string newName)
    {
        oldName = string.Empty;
        newName = string.Empty;
        var m = Regex.Match(lower, @"\brename\b\s+(?:the\s+)?(?:file|folder\s+)?(?:named\s+)?[""']?(?<old>[^""']+)[""']?\s+(?:to|as)\s+[""']?(?<new>[^""'!?.]+)[""']?", RegexOptions.IgnoreCase);
        if (m.Success && m.Groups["old"].Success && m.Groups["new"].Success)
        {
            oldName = m.Groups["old"].Value.Trim();
            newName = m.Groups["new"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(oldName) && !string.IsNullOrWhiteSpace(newName)) return true;
        }
        return false;
    }

    private static bool TryMatchMoveOrCopyCommand(string lower, out bool isCopy, out string sourceName, out string destLocation)
    {
        isCopy = false;
        sourceName = string.Empty;
        destLocation = string.Empty;
        
        var m = Regex.Match(lower, @"\b(?<action>move|copy)\b\s+(?:the\s+)?(?:file|folder\s+)?(?:named\s+)?[""']?(?<src>[^""']+)[""']?\s+(?:to|into)\s+(?:the\s+)?(?:folder\s+)?[""']?(?<dest>[^""'!?.]+)[""']?", RegexOptions.IgnoreCase);
        if (m.Success && m.Groups["action"].Success && m.Groups["src"].Success && m.Groups["dest"].Success)
        {
            isCopy = m.Groups["action"].Value.Equals("copy", StringComparison.OrdinalIgnoreCase);
            sourceName = m.Groups["src"].Value.Trim();
            destLocation = m.Groups["dest"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(sourceName) && !string.IsNullOrWhiteSpace(destLocation)) return true;
        }
        return false;
    }

    public void SetLastAssistantResponse(string assistantResponse)
    {
        _pendingShellCommand = ExtractShellCommandFromText(assistantResponse);
        _pendingShellCommandOptions = ExtractShellCommandOptionsFromText(assistantResponse);
    }

    public async Task<string?> TryExecuteAssistantCommandAsync(string assistantResponse)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(assistantResponse))
            return null;

        var command = ExtractShellCommandFromText(assistantResponse);
        if (!string.IsNullOrWhiteSpace(command))
        {
            _pendingShellCommand = command;
            _pendingShellCommandOptions = ExtractShellCommandOptionsFromText(assistantResponse);
            var result = await ExecuteShellCommandAsync(command);
            return $"[Executed shell command: `{command}`]\n{result}";
        }

        // Do NOT perform any fallback file/folder creation based on the assistant's response text.
        // AI responses can contain words like 'create' and 'folder' in explanatory context
        // and matching against them causes spurious filesystem side-effects.
        return null;
    }

    private static List<string> ExtractShellCommandOptionsFromText(string text)
    {
        var options = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return options;

        var matches = Regex.Matches(text, @"^\s*(\d+)[\.)]\s*(.+)$", RegexOptions.Multiline);
        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 3)
                continue;

            var optionText = match.Groups[2].Value.Trim();
            var command = ExtractShellCommandFromText(optionText) ?? ExtractFirstCommandLine(optionText);
            if (!string.IsNullOrWhiteSpace(command))
                options.Add(command);
        }

        return options;
    }

    private static string? ExtractShellCommandFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var fenceMatch = Regex.Match(text, @"```(?:cmd|powershell|bash|sh)?\s*\r?\n(?<cmd>.*?)\r?\n```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fenceMatch.Success && fenceMatch.Groups["cmd"].Success)
            return ExtractFirstCommandLine(fenceMatch.Groups["cmd"].Value.Trim());

        var commandBlockMatch = Regex.Match(text, @"(?:command|cmd|powershell)\s*[:\-]\s*\r?\n(?<cmd>.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (commandBlockMatch.Success && commandBlockMatch.Groups["cmd"].Success)
            return ExtractFirstCommandLine(commandBlockMatch.Groups["cmd"].Value.Trim());

        var inlineMatch = Regex.Match(text, "`(?<cmd>[^`\r\n]+)`");
        if (inlineMatch.Success && inlineMatch.Groups["cmd"].Success)
            return inlineMatch.Groups["cmd"].Value.Trim();

        var directMatch = Regex.Match(text, @"\b(?:run|execute)\s+(?<cmd>(?:[A-Za-z0-9_./\\:-]+\s*)+)", RegexOptions.IgnoreCase);
        if (directMatch.Success && directMatch.Groups["cmd"].Success)
        {
            var candidate = directMatch.Groups["cmd"].Value.Trim();
            if (ContainsKnownCommand(candidate)) return ExtractFirstCommandLine(candidate);
        }

        var parenthesisMatch = Regex.Match(text, "[(\\\"']\\s*(?<cmd>(?:sfc|chkdsk|netsh|ipconfig|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|shutdown|gpupdate|powershell|cmd)\\b[^\\)\\\"']*)[\\)\\\"']", RegexOptions.IgnoreCase);
        if (parenthesisMatch.Success && parenthesisMatch.Groups["cmd"].Success)
            return parenthesisMatch.Groups["cmd"].Value.Trim();

        var anywhereMatch = Regex.Match(text, @"\b(?<cmd>(?:sfc|chkdsk|netsh|ipconfig|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|shutdown|gpupdate|powershell|cmd)\b[^\r\n]*)", RegexOptions.IgnoreCase);
        if (anywhereMatch.Success && anywhereMatch.Groups["cmd"].Success)
            return anywhereMatch.Groups["cmd"].Value.Trim();

        return null;
    }

    private static bool ContainsKnownCommand(string text)
    {
        return Regex.IsMatch(text, @"\b(?:sfc|chkdsk|netsh|ipconfig|tasklist|taskkill|robocopy|xcopy|reg|bcdedit|bootrec|systeminfo|wmic|ping|tracert|shutdown|gpupdate|powershell|cmd)\b", RegexOptions.IgnoreCase);
    }

    private static string? ExtractFirstCommandLine(string candidate)
    {
        var lines = candidate.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return null;

        var first = lines[0];
        if (first.Length > 300)
            return null;

        return first;
    }

    private async Task<string> OpenFileAsync(string fileNameHint)
    {
        try
        {
            var searchRoots = new[]
            {
                GetDefaultDesktopFolder(),
                _downloadsFolder,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            var candidates = searchRoots
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
                .Where(f => Path.GetFileName(f).Contains(fileNameHint, StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileNameWithoutExtension(f).Contains(fileNameHint, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
                return $"I couldn't find a file or folder matching '{fileNameHint}' on your Desktop, Documents, or Downloads.";

            var target = candidates.First();
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return $"Opened: {Path.GetFileName(target)}";
            }
            catch (Exception ex)
            {
                return $"Couldn't open the file/folder: {ex.Message}";
            }
        }
        catch
        {
            return "I couldn't search for the file.";
        }
    }

    private async Task<string> DeleteItemAsync(string targetName)
    {
        var target = FindItemInCommonFolders(targetName);
        if (target == null) return $"I couldn't find '{targetName}' to delete.";

        try
        {
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            else if (File.Exists(target))
                File.Delete(target);
            
            return $"✅ Deleted: `{target}`";
        }
        catch (Exception ex)
        {
            return $"❌ Failed to delete `{target}`: {ex.Message}";
        }
    }

    private async Task<string> RenameItemAsync(string oldName, string newName)
    {
        var target = FindItemInCommonFolders(oldName);
        if (target == null) return $"I couldn't find '{oldName}' to rename.";

        try
        {
            var directory = Path.GetDirectoryName(target);
            var extension = Path.GetExtension(target);
            if (!Path.HasExtension(newName) && !string.IsNullOrEmpty(extension) && File.Exists(target))
                newName += extension;

            var newPath = Path.Combine(directory!, newName);

            if (Directory.Exists(target))
                Directory.Move(target, newPath);
            else if (File.Exists(target))
                File.Move(target, newPath);
            
            return $"✅ Renamed `{oldName}` to `{newName}`.";
        }
        catch (Exception ex)
        {
            return $"❌ Failed to rename `{target}`: {ex.Message}";
        }
    }

    private async Task<string> MoveOrCopyItemAsync(string sourceName, string destFolder, bool isCopy)
    {
        var target = FindItemInCommonFolders(sourceName);
        if (target == null) return $"I couldn't find '{sourceName}' to {(isCopy ? "copy" : "move")}.";

        if (!Directory.Exists(destFolder))
        {
            try { Directory.CreateDirectory(destFolder); }
            catch { return $"❌ Destination folder `{destFolder}` does not exist and could not be created."; }
        }

        try
        {
            var fileName = Path.GetFileName(target);
            var destPath = Path.Combine(destFolder, fileName);
            destPath = GetUniqueDestinationPath(destPath);

            var actionName = isCopy ? "Copied" : "Moved";

            if (Directory.Exists(target))
            {
                if (isCopy)
                    CopyDirectory(target, destPath);
                else
                    Directory.Move(target, destPath);
            }
            else if (File.Exists(target))
            {
                if (isCopy)
                    File.Copy(target, destPath);
                else
                    File.Move(target, destPath);
            }
            
            return $"✅ {actionName} `{fileName}` to `{destFolder}`.";
        }
        catch (Exception ex)
        {
            return $"❌ Failed to {(isCopy ? "copy" : "move")} `{target}`: {ex.Message}";
        }
    }

    private string? FindItemInCommonFolders(string nameHint)
    {
        var searchRoots = new[]
        {
            GetDefaultDesktopFolder(),
            _downloadsFolder,
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        return searchRoots
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
            .FirstOrDefault(f => Path.GetFileName(f).Equals(nameHint, StringComparison.OrdinalIgnoreCase)
                              || Path.GetFileNameWithoutExtension(f).Equals(nameHint, StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
