First preview of Diskpart+, a modern GUI for Windows' built-in diskpart.

## Download

- **DiskpartPlus-…-win-x64.exe** for almost every Windows PC
- **DiskpartPlus-…-win-arm64.exe** for ARM PCs (Snapdragon / Copilot+ laptops)

It's a single file. No installer and nothing else to download.

## Running it

1. Double-click the .exe. Windows asks for administrator rights because diskpart needs them.
2. The app isn't code-signed yet, so Windows may show **"Windows protected your PC"**. Click **More info** and then **Run anyway**. The admin prompt will also say "Unknown publisher".

## What's in it

- Every disk, partition and volume, with Mac (APFS, HFS+), Linux and Windows partition types named properly
- **Wipe and reuse** for drives that came from a Mac or had Linux on them
- Create, delete, format, extend and shrink partitions
- Assign, change and remove drive letters
- Delete protected partitions (EFI, recovery, OEM) that Disk Management won't touch
- `clean` and `clean all`, GPT and MBR conversion, read-only flag, online and offline, disk IDs, partition types and GPT attributes
- Create, attach and detach VHD and VHDX files
- A review screen before every change showing the risk, warnings and the exact diskpart script, with typed confirmation for anything that erases data
- Light and dark themes that follow Windows

## Before you use it on a real drive

Try it on a virtual disk first: **Create virtual disk...** makes one in a few seconds and you can wipe it as often as you like. Keep backups of anything that matters.

## Known limitations

- Not code-signed yet, hence the SmartScreen warning.
- It can wipe Mac drives but can't open or copy files from APFS or HFS+. Windows has no driver for them.
- Labels and file paths can only use characters your Windows console code page supports. Diskpart+ tells you before running if one won't work.
- Every diskpart operation is tested automatically against real diskpart on Windows virtual machines, but the app hasn't been tried on a wide range of real hardware yet. Please report anything odd.
